using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pug.Application.Data;
using Pug.Application.Security;
using Pug.Effable;
using Pug.Sal.Abstractions;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// The in-process ILocalityAdministration (DECISIONS D16): reads over the full records, and the
	// writes. Each write to a country runs in one transaction that first takes the country's lock
	// and next version (D13, D15); the version is the write's action timestamp. Changes are published
	// after commit, best-effort (D7).
	//
	// Retiring a retired record, or reinstating a current one, changes nothing: the record is
	// returned as it is, with no version bump and nothing published.
	public sealed class LocalityAdministration : LocalityServiceBase, ILocalityAdministration
	{
		private readonly IDatasetImporterRegistry _importers;

		private readonly ILocalityChangeEventPublisher _eventPublisher;

		private readonly ILogger<LocalityAdministration> _logger;

		private readonly ImportCoordinator _coordinator;

		public LocalityAdministration( IApplicationData<IDataSession> applicationData,
										ISecurityManager securityManager,
										INameNormalizer normalizer,
										IDatasetImporterRegistry importers,
										ILocalityChangeEventPublisher eventPublisher,
										ILogger<LocalityAdministration>? logger = null )
			: base( applicationData, securityManager, normalizer )
		{
			_importers = importers ?? throw new ArgumentNullException( nameof(importers) );
			_eventPublisher = eventPublisher ?? throw new ArgumentNullException( nameof(eventPublisher) );
			_logger = logger ?? NullLogger<LocalityAdministration>.Instance;
			_coordinator = new ImportCoordinator( normalizer );
		}

		// == Reads (VIEW) ==

		public Task<IEnumerable<Country>> GetCountriesAsync( bool includeRetired = false ) => ReadCountriesAsync( includeRetired );

		public Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync() => ReadCountryVersionsAsync();

		public Task<Country> GetCountryAsync( string country ) => ReadCountryAsync( country );

		public Task<Versioned<IEnumerable<Subdivision>>> GetSubdivisionsAsync( string country, bool includeRetired = false ) =>
			ReadSubdivisionsAsync( country, includeRetired );

		public Task<Versioned<Subdivision>> GetSubdivisionAsync( string country, string subdivision ) =>
			ReadSubdivisionAsync( country, subdivision );

		public Task<IEnumerable<Category>> GetCategoriesAsync( bool includeRetired = false ) => ReadCategoriesAsync( includeRetired );

		public async Task<Category> GetCategoryAsync( string category )
		{
			await CheckAuthorizationAsync( SecurityOperations.View, ResourceQualifiers.AllCategories ).ConfigureAwait( false );

			string code = RequiredCategory( category, nameof(category) );

			return await ReadCategoryAsync( code ).ConfigureAwait( false ) ?? throw new UnknownCategoryException( null!, code );
		}

		public Task<Versioned<IEnumerable<CountryCategory>>> GetCountryCategoriesAsync( string country, bool includeDisabled = true ) =>
			ReadCountryCategoriesAsync( country, includeDisabled );

		public Task<Versioned<IEnumerable<Locality>>> GetLocalitiesAsync( string country, string subdivision = null!,
																		string postcode = null!, string name = null!,
																		string category = null!, bool includeRetired = false ) =>
			ReadLocalitiesAsync( country, subdivision, postcode, name, category, includeRetired );

		public Task<Versioned<Locality>> GetLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			ReadLocalityAsync( country, subdivision, postcode, name );

		public Task<Versioned<IEnumerable<Locality>>> SearchLocalitiesAsync( string country, string text, string subdivision = null!,
																			string category = null!, int limit = 20 ) =>
			SearchAsync( country, text, subdivision, category, limit );

		// == Countries ==

		public async Task<Country> CreateCountryAsync( CountryDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Create ).ConfigureAwait( false );

			CountryDefinition stored = DefinitionRules.Country( definition );
			IReadOnlyList<string> categories = DefinitionRules.Categories( definition.Categories );

			// A new country has no lock to take: its first version is simply the next after none.
			ActionContext<Reference> action = Action( DatasetVersions.Next( DateTime.MinValue, DateTime.UtcNow ) );
			Country country = new() { Definition = stored, CurrentVersion = action.Timestamp, Registration = action };

			try
			{
				await ApplicationDataProvider.WriteAsync(
												async ( session, context ) =>
												{
													await session.InsertCountryAsync( context.country ).ConfigureAwait( false );

													// Its first categories, assigned enabled (D28).
													foreach( string code in context.categories )
													{
														Category category = await CurrentCategoryAsync( session, code ).ConfigureAwait( false );

														await session.InsertCountryCategoryAsync( new CountryCategory
																	{
																		Country = context.country.Definition.Code,
																		Category = code,
																		Name = category.Definition.Name,
																		Registration = context.action
																	} )
																	.ConfigureAwait( false );
													}

													return true;
												},
												context: new { country, categories, action }
											)
											.ConfigureAwait( false );
			}
			catch( DuplicateKeyException )
			{
				throw new DuplicateEntryException( ChangeEntityTypes.Country, stored.Code );
			}

			await PublishAsync( CountryChange( stored.Code, ChangeKinds.Created, action ) ).ConfigureAwait( false );

			foreach( string category in categories )
				await PublishAsync( CountryCategoryChange( stored.Code, category, ChangeKinds.Created, action ) ).ConfigureAwait( false );

			return country;
		}

		public async Task<Country> UpdateCountryAsync( CountryDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Modify ).ConfigureAwait( false );

			CountryDefinition stored = DefinitionRules.Country( definition );

			Written<Country> written = await WriteAsync(
											stored.Code,
											async ( session, action ) =>
											{
												Country current = ( await session.GetCountryAsync( stored.Code ).ConfigureAwait( false ) )!;

												if( current.Definition.HasSubdivisions != stored.HasSubdivisions &&
													await session.HasLocalitiesAsync( stored.Code ).ConfigureAwait( false ) )
												{
													throw new ArgumentException(
														$"HasSubdivisions of country '{stored.Code}' cannot change while it holds localities.",
														nameof(definition) );
												}

												await session.UpdateCountryAsync( stored, action ).ConfigureAwait( false );

												return ( await session.GetCountryAsync( stored.Code ).ConfigureAwait( false ) )!;
											}
										)
										.ConfigureAwait( false );

			await PublishAsync( CountryChange( stored.Code, ChangeKinds.Updated, written.Performed ) ).ConfigureAwait( false );

			return written.Value;
		}

		public Task<Country> RetireCountryAsync( string country ) => SetCountryRetiredAsync( country, true );

		public Task<Country> ReinstateCountryAsync( string country ) => SetCountryRetiredAsync( country, false );

		private async Task<Country> SetCountryRetiredAsync( string country, bool retire )
		{
			await CheckAuthorizationAsync( SecurityOperations.Retire ).ConfigureAwait( false );

			string code = CountryCode( country );
			Country current = await ReadCountryValueAsync( code, ( _, found ) => Task.FromResult<Country?>( found ) ).ConfigureAwait( false ) ??
							throw new UnknownCountryException( code );

			if( ( current.Retired is not null ) == retire )
				return current;

			Written<Country> written = await WriteAsync(
											code,
											async ( session, action ) =>
											{
												await session.SetCountryRetiredAsync( code, retire ? action.Timestamp : null, action )
															.ConfigureAwait( false );

												return ( await session.GetCountryAsync( code ).ConfigureAwait( false ) )!;
											}
										)
										.ConfigureAwait( false );

			await PublishAsync( CountryChange( code, retire ? ChangeKinds.Retired : ChangeKinds.Reinstated, written.Performed ) )
				.ConfigureAwait( false );

			return written.Value;
		}

		// == Subdivisions ==

		public async Task<Versioned<Subdivision>> CreateSubdivisionAsync( SubdivisionDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Create ).ConfigureAwait( false );

			SubdivisionDefinition stored = DefinitionRules.Subdivision( definition );

			Written<Subdivision> written = await WriteAsync(
												stored.Country,
												async ( session, action ) =>
												{
													Country country = ( await session.GetCountryAsync( stored.Country ).ConfigureAwait( false ) )!;

													if( !country.Definition.HasSubdivisions )
														throw new ArgumentException( $"Country '{stored.Country}' has no subdivisions.", nameof(definition) );

													Subdivision subdivision = new() { Definition = stored, Registration = action };

													try
													{
														await session.InsertSubdivisionAsync( subdivision ).ConfigureAwait( false );
													}
													catch( DuplicateKeyException )
													{
														throw new DuplicateEntryException( ChangeEntityTypes.Subdivision, stored.Code );
													}

													return subdivision;
												}
											)
											.ConfigureAwait( false );

			await PublishAsync( SubdivisionChange( stored.Country, stored.Code, ChangeKinds.Created, written.Performed ) )
				.ConfigureAwait( false );

			return written.Versioned();
		}

		public async Task<Versioned<Subdivision>> UpdateSubdivisionAsync( SubdivisionDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Modify ).ConfigureAwait( false );

			SubdivisionDefinition stored = DefinitionRules.Subdivision( definition );

			Written<Subdivision> written = await WriteAsync(
												stored.Country,
												async ( session, action ) =>
												{
													if( !await session.UpdateSubdivisionAsync( stored, action ).ConfigureAwait( false ) )
														throw new UnknownSubdivisionException( stored.Country, stored.Code );

													return ( await session.GetSubdivisionAsync( stored.Country, stored.Code ).ConfigureAwait( false ) )!;
												}
											)
											.ConfigureAwait( false );

			await PublishAsync( SubdivisionChange( stored.Country, stored.Code, ChangeKinds.Updated, written.Performed ) )
				.ConfigureAwait( false );

			return written.Versioned();
		}

		public Task<Versioned<Subdivision>> RetireSubdivisionAsync( string country, string subdivision ) =>
			SetSubdivisionRetiredAsync( country, subdivision, true );

		public Task<Versioned<Subdivision>> ReinstateSubdivisionAsync( string country, string subdivision ) =>
			SetSubdivisionRetiredAsync( country, subdivision, false );

		private async Task<Versioned<Subdivision>> SetSubdivisionRetiredAsync( string country, string subdivision, bool retire )
		{
			await CheckAuthorizationAsync( SecurityOperations.Retire ).ConfigureAwait( false );

			string code = CountryCode( country );
			string subdivisionCode = OptionalCode( subdivision ) ??
									throw new ArgumentException( "A subdivision code is required.", nameof(subdivision) );

			Versioned<Subdivision?> current = await ReadVersionedAsync(
												code,
												( session, _ ) => session.GetSubdivisionAsync( code, subdivisionCode )
											)
											.ConfigureAwait( false );

			if( current.Value is null )
				throw new UnknownSubdivisionException( code, subdivisionCode );

			if( ( current.Value.Retired is not null ) == retire )
				return new Versioned<Subdivision> { Value = current.Value, DatasetVersion = current.DatasetVersion };

			Written<Subdivision> written = await WriteAsync(
												code,
												async ( session, action ) =>
												{
													if( !await session.SetSubdivisionRetiredAsync( code, subdivisionCode, retire ? action.Timestamp : null, action )
																	.ConfigureAwait( false ) )
														throw new UnknownSubdivisionException( code, subdivisionCode );

													return ( await session.GetSubdivisionAsync( code, subdivisionCode ).ConfigureAwait( false ) )!;
												}
											)
											.ConfigureAwait( false );

			await PublishAsync( SubdivisionChange( code, subdivisionCode, retire ? ChangeKinds.Retired : ChangeKinds.Reinstated,
													written.Performed ) )
				.ConfigureAwait( false );

			return written.Versioned();
		}

		// == Localities ==

		public async Task<Versioned<Locality>> CreateLocalityAsync( LocalityDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Create ).ConfigureAwait( false );

			LocalityDefinition stored = DefinitionRules.Locality( definition );
			string nameKey = NameKey( stored.Name )!;
			string? postcodeKey = Normalizer.NormalizePostcode( stored.Postcode );

			Written<Locality> written = await WriteAsync(
											stored.Country,
											async ( session, action ) =>
											{
												Country country = ( await session.GetCountryAsync( stored.Country ).ConfigureAwait( false ) )!;

												if( country.Definition.HasSubdivisions )
												{
													if( stored.Subdivision is null )
														throw new ArgumentException( $"A subdivision is required in country '{stored.Country}'.", nameof(definition) );

													if( await session.GetSubdivisionAsync( stored.Country, stored.Subdivision ).ConfigureAwait( false ) is null )
														throw new UnknownSubdivisionException( stored.Country, stored.Subdivision );
												}
												else if( stored.Subdivision is not null )
												{
													throw new ArgumentException( $"Country '{stored.Country}' has no subdivisions.", nameof(definition) );
												}

												string? problem = PostcodeRules.Problem( country.Definition, postcodeKey );

												if( problem is not null )
													throw new ArgumentException( problem, nameof(definition) );

												CountryCategory category =
													await session.GetCountryCategoryAsync( stored.Country, stored.Category ).ConfigureAwait( false ) ??
													throw new UnknownCategoryException( stored.Country, stored.Category );

												if( category.Disabled is not null )
													throw new ArgumentException(
														$"Category '{stored.Category}' is disabled in country '{stored.Country}'.", nameof(definition) );

												Locality locality = new() { Definition = stored, Registration = action };

												try
												{
													await session.InsertLocalityAsync( locality, postcodeKey, nameKey ).ConfigureAwait( false );
												}
												catch( DuplicateKeyException )
												{
													throw new DuplicateEntryException( ChangeEntityTypes.Locality,
														$"{stored.Subdivision}/{stored.Postcode}/{stored.Name}" );
												}

												return locality;
											}
										)
										.ConfigureAwait( false );

			await PublishAsync( LocalityChange( written.Value.Definition, ChangeKinds.Created, written.Performed ) ).ConfigureAwait( false );

			return written.Versioned();
		}

		public Task<Versioned<Locality>> RetireLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			SetLocalityRetiredAsync( country, subdivision, postcode, name, true );

		public Task<Versioned<Locality>> ReinstateLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			SetLocalityRetiredAsync( country, subdivision, postcode, name, false );

		private async Task<Versioned<Locality>> SetLocalityRetiredAsync( string country, string subdivision, string postcode,
																		string name, bool retire )
		{
			await CheckAuthorizationAsync( SecurityOperations.Retire ).ConfigureAwait( false );

			LocalityKey key = KeyOf( CountryCode( country ), subdivision, postcode, name );

			Versioned<Locality?> current = await ReadVersionedAsync( key.Country, ( session, _ ) => session.GetLocalityAsync( key ) )
											.ConfigureAwait( false );

			if( current.Value is null )
				throw Unknown( key, postcode, name );

			if( ( current.Value.Retired is not null ) == retire )
				return new Versioned<Locality> { Value = current.Value, DatasetVersion = current.DatasetVersion };

			Written<Locality> written = await WriteAsync(
											key.Country,
											async ( session, action ) =>
											{
												if( !await session.SetLocalityRetiredAsync( key, retire ? action.Timestamp : null, action )
																.ConfigureAwait( false ) )
													throw Unknown( key, postcode, name );

												return ( await session.GetLocalityAsync( key ).ConfigureAwait( false ) )!;
											}
										)
										.ConfigureAwait( false );

			await PublishAsync( LocalityChange( written.Value.Definition, retire ? ChangeKinds.Retired : ChangeKinds.Reinstated,
												written.Performed ) )
				.ConfigureAwait( false );

			return written.Versioned();
		}

		public async Task<DateTime> DeleteLocalityAsync( string country, string subdivision, string postcode, string name )
		{
			await CheckAuthorizationAsync( SecurityOperations.Delete ).ConfigureAwait( false );

			LocalityKey key = KeyOf( CountryCode( country ), subdivision, postcode, name );

			Written<Locality> written = await WriteAsync(
											key.Country,
											async ( session, _ ) =>
											{
												Locality locality = await session.GetLocalityAsync( key ).ConfigureAwait( false ) ??
																	throw Unknown( key, postcode, name );

												await session.DeleteLocalityAsync( key ).ConfigureAwait( false );

												return locality;
											}
										)
										.ConfigureAwait( false );

			await PublishAsync( LocalityChange( written.Value.Definition, ChangeKinds.Deleted, written.Performed ) ).ConfigureAwait( false );

			return written.Performed.Timestamp;
		}

		// == Imports ==

		public async Task<ImportRun> ImportAsync( string country, ImportRequest request, Stream source )
		{
			await CheckAuthorizationAsync( SecurityOperations.Import ).ConfigureAwait( false );

			string code = CountryCode( country );

			if( request is null )
				throw new ArgumentNullException( nameof(request) );

			if( source is null )
				throw new ArgumentNullException( nameof(source) );

			if( string.IsNullOrWhiteSpace( request.Importer ) )
				throw new ArgumentException( "An importer name is required.", nameof(request) );

			string importerName = request.Importer.Trim();
			IDatasetImporter importer = _importers.GetImporter( importerName ) ??
										throw new UnknownDatasetImporterException( importerName );

			// Unknown countries are refused before the dataset is read.
			_ = await ReadCountryValueAsync( code, ( _, found ) => Task.FromResult<Country?>( found ) ).ConfigureAwait( false ) ??
				throw new UnknownCountryException( code );

			List<ImportRow> rows = new();

			await foreach( ImportRow row in importer.ReadAsync( code, source, request.Options ?? new Dictionary<string, string>() )
													.ConfigureAwait( false ) )
				rows.Add( row );

			Written<ImportRun> written = await WriteAsync(
											code,
											( session, action ) =>
												_coordinator.ApplyAsync( session, code, importerName, request.Source, rows, action )
										)
										.ConfigureAwait( false );

			await PublishAsync( publisher => publisher.PublishImportAsync( written.Value ), written.Value.Country )
				.ConfigureAwait( false );

			return written.Value;
		}

		public async Task<IEnumerable<ImportRun>> GetImportRunsAsync( string country )
		{
			await CheckAuthorizationAsync( SecurityOperations.Import ).ConfigureAwait( false );

			string code = CountryCode( country );

			Versioned<IEnumerable<ImportRun>> runs = await ReadVersionedAsync( code, ( session, _ ) => session.GetImportRunsAsync( code ) )
														.ConfigureAwait( false );

			return runs.Value;
		}

		// == Global categories (D28) ==

		public async Task<Category> CreateCategoryAsync( CategoryDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Create, ResourceQualifiers.AllCategories ).ConfigureAwait( false );

			CategoryDefinition stored = DefinitionRules.Category( definition );
			ActionContext<Reference> action = Action( DateTime.UtcNow );
			Category category = new() { Definition = stored, Registration = action };

			try
			{
				await ApplicationDataProvider.WriteAsync(
												async ( session, context ) =>
												{
													await session.InsertCategoryAsync( context.category ).ConfigureAwait( false );

													return true;
												},
												context: new { category }
											)
											.ConfigureAwait( false );
			}
			catch( DuplicateKeyException )
			{
				throw new DuplicateEntryException( ChangeEntityTypes.Category, stored.Code );
			}

			return category;
		}

		public async Task<Category> UpdateCategoryAsync( CategoryDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Modify, ResourceQualifiers.AllCategories ).ConfigureAwait( false );

			CategoryDefinition stored = DefinitionRules.Category( definition );

			Written<Category> written = await WriteCategoryAsync(
											async ( session, action ) =>
											{
												if( !await session.UpdateCategoryAsync( stored, action ).ConfigureAwait( false ) )
													throw new UnknownCategoryException( null!, stored.Code );

												return ( await session.GetCategoryAsync( stored.Code ).ConfigureAwait( false ) )!;
											}
										)
										.ConfigureAwait( false );

			return written.Value;
		}

		public Task<Category> RetireCategoryAsync( string category ) => SetCategoryRetiredAsync( category, true );

		public Task<Category> ReinstateCategoryAsync( string category ) => SetCategoryRetiredAsync( category, false );

		private async Task<Category> SetCategoryRetiredAsync( string category, bool retire )
		{
			await CheckAuthorizationAsync( SecurityOperations.Retire, ResourceQualifiers.AllCategories ).ConfigureAwait( false );

			string code = RequiredCategory( category, nameof(category) );
			Category current = await ReadCategoryAsync( code ).ConfigureAwait( false ) ?? throw new UnknownCategoryException( null!, code );

			if( ( current.Retired is not null ) == retire )
				return current;

			Written<Category> written = await WriteCategoryAsync(
											async ( session, action ) =>
											{
												if( !await session.SetCategoryRetiredAsync( code, retire ? action.Timestamp : null, action )
																.ConfigureAwait( false ) )
													throw new UnknownCategoryException( null!, code );

												return ( await session.GetCategoryAsync( code ).ConfigureAwait( false ) )!;
											}
										)
										.ConfigureAwait( false );

			return written.Value;
		}

		// == Country categories (D28) ==

		public async Task<Versioned<CountryCategory>> AssignCategoryAsync( string country, string category )
		{
			await CheckAuthorizationAsync( SecurityOperations.Modify ).ConfigureAwait( false );

			string code = CountryCode( country );
			string categoryCode = RequiredCategory( category, nameof(category) );

			Versioned<CountryCategory?> current = await ReadVersionedAsync( code, ( session, _ ) => session.GetCountryCategoryAsync( code, categoryCode ) )
													.ConfigureAwait( false );

			if( current.Value is not null )
				return new Versioned<CountryCategory> { Value = current.Value, DatasetVersion = current.DatasetVersion };

			Written<CountryCategory> written = await WriteAsync(
													code,
													async ( session, action ) =>
													{
														Category global = await CurrentCategoryAsync( session, categoryCode ).ConfigureAwait( false );

														try
														{
															await session.InsertCountryCategoryAsync( new CountryCategory
																		{
																			Country = code,
																			Category = categoryCode,
																			Name = global.Definition.Name,
																			Registration = action
																		} )
																		.ConfigureAwait( false );
														}
														catch( DuplicateKeyException )
														{
															throw new DuplicateEntryException( ChangeEntityTypes.CountryCategory, categoryCode );
														}

														return ( await session.GetCountryCategoryAsync( code, categoryCode ).ConfigureAwait( false ) )!;
													}
												)
												.ConfigureAwait( false );

			await PublishAsync( CountryCategoryChange( code, categoryCode, ChangeKinds.Created, written.Performed ) ).ConfigureAwait( false );

			return written.Versioned();
		}

		public async Task<DateTime> UnassignCategoryAsync( string country, string category )
		{
			await CheckAuthorizationAsync( SecurityOperations.Modify ).ConfigureAwait( false );

			string code = CountryCode( country );
			string categoryCode = RequiredCategory( category, nameof(category) );

			Written<CountryCategory> written = await WriteAsync(
													code,
													async ( session, _ ) =>
													{
														CountryCategory assigned =
															await session.GetCountryCategoryAsync( code, categoryCode ).ConfigureAwait( false ) ??
															throw new UnknownCategoryException( code, categoryCode );

														if( await session.HasLocalitiesAsync( code, categoryCode ).ConfigureAwait( false ) )
															throw new ArgumentException(
																$"Category '{categoryCode}' of country '{code}' is held by localities; disable it instead.",
																nameof(category) );

														if( assigned.Disabled is null )
															await RequireAnotherEnabledAsync( session, code, categoryCode ).ConfigureAwait( false );

														await session.DeleteCountryCategoryAsync( code, categoryCode ).ConfigureAwait( false );

														return assigned;
													}
												)
												.ConfigureAwait( false );

			await PublishAsync( CountryCategoryChange( code, categoryCode, ChangeKinds.Deleted, written.Performed ) ).ConfigureAwait( false );

			return written.Performed.Timestamp;
		}

		public async Task<Versioned<CountryCategory>> DisableCategoryAsync( string country, string category, string replacement = null! )
		{
			await CheckAuthorizationAsync( SecurityOperations.Retire ).ConfigureAwait( false );

			string code = CountryCode( country );
			string categoryCode = RequiredCategory( category, nameof(category) );
			string? replacementCode = OptionalCode( replacement );

			Versioned<(CountryCategory? Category, bool Held)> current = await ReadVersionedAsync(
																			code,
																			async ( session, _ ) =>
																				( await session.GetCountryCategoryAsync( code, categoryCode ).ConfigureAwait( false ),
																				await session.HasLocalitiesAsync( code, categoryCode ).ConfigureAwait( false ) ) )
																		.ConfigureAwait( false );

			if( current.Value.Category is null )
				throw new UnknownCategoryException( code, categoryCode );

			// A disabled category changes only when a replacement has localities to move.
			if( current.Value.Category.Disabled is not null && ( replacementCode is null || !current.Value.Held ) )
				return new Versioned<CountryCategory> { Value = current.Value.Category, DatasetVersion = current.DatasetVersion };

			Written<(CountryCategory Category, bool Disabled, int Moved)> written = await WriteAsync(
													code,
													async ( session, action ) =>
													{
														CountryCategory assigned =
															await session.GetCountryCategoryAsync( code, categoryCode ).ConfigureAwait( false ) ??
															throw new UnknownCategoryException( code, categoryCode );

														if( replacementCode is not null )
														{
															if( replacementCode == categoryCode )
																throw new ArgumentException( "A category cannot replace itself.", nameof(replacement) );

															CountryCategory target =
																await session.GetCountryCategoryAsync( code, replacementCode ).ConfigureAwait( false ) ??
																throw new UnknownCategoryException( code, replacementCode );

															if( target.Disabled is not null )
																throw new ArgumentException(
																	$"Replacement category '{replacementCode}' is disabled in country '{code}'.", nameof(replacement) );
														}
														else
														{
															await RequireAnotherEnabledAsync( session, code, categoryCode ).ConfigureAwait( false );
														}

														if( assigned.Disabled is null )
															await session.SetCountryCategoryDisabledAsync( code, categoryCode, action.Timestamp, action )
																		.ConfigureAwait( false );

														int moved = replacementCode is null
																		? 0
																		: await session.ReplaceLocalityCategoryAsync( code, categoryCode, replacementCode, action )
																					.ConfigureAwait( false );

														return ( ( await session.GetCountryCategoryAsync( code, categoryCode ).ConfigureAwait( false ) )!,
																assigned.Disabled is null, moved );
													}
												)
												.ConfigureAwait( false );

			if( written.Value.Disabled )
				await PublishAsync( CountryCategoryChange( code, categoryCode, ChangeKinds.Retired, written.Performed ) ).ConfigureAwait( false );

			// Moved localities publish one replacement event, not a change each (D28).
			if( written.Value.Moved > 0 )
			{
				CategoryReplacement moved = new()
				{
					Country = code,
					Category = categoryCode,
					Replacement = replacementCode!,
					Localities = written.Value.Moved,
					DatasetVersion = written.Performed.Timestamp,
					Performed = written.Performed
				};

				await PublishAsync( publisher => publisher.PublishCategoryReplacementAsync( moved ), code ).ConfigureAwait( false );
			}

			return new Versioned<CountryCategory> { Value = written.Value.Category, DatasetVersion = written.Performed.Timestamp };
		}

		public async Task<Versioned<CountryCategory>> EnableCategoryAsync( string country, string category )
		{
			await CheckAuthorizationAsync( SecurityOperations.Retire ).ConfigureAwait( false );

			string code = CountryCode( country );
			string categoryCode = RequiredCategory( category, nameof(category) );

			Versioned<CountryCategory?> current = await ReadVersionedAsync( code, ( session, _ ) => session.GetCountryCategoryAsync( code, categoryCode ) )
													.ConfigureAwait( false );

			if( current.Value is null )
				throw new UnknownCategoryException( code, categoryCode );

			if( current.Value.Disabled is null )
				return new Versioned<CountryCategory> { Value = current.Value, DatasetVersion = current.DatasetVersion };

			Written<CountryCategory> written = await WriteAsync(
													code,
													async ( session, action ) =>
													{
														await CurrentCategoryAsync( session, categoryCode ).ConfigureAwait( false );

														if( !await session.SetCountryCategoryDisabledAsync( code, categoryCode, null, action ).ConfigureAwait( false ) )
															throw new UnknownCategoryException( code, categoryCode );

														return ( await session.GetCountryCategoryAsync( code, categoryCode ).ConfigureAwait( false ) )!;
													}
												)
												.ConfigureAwait( false );

			await PublishAsync( CountryCategoryChange( code, categoryCode, ChangeKinds.Reinstated, written.Performed ) )
				.ConfigureAwait( false );

			return written.Versioned();
		}

		// The global category, which must exist and be current to be assigned or enabled.
		private static async Task<Category> CurrentCategoryAsync( IDataSession session, string code )
		{
			Category category = await session.GetCategoryAsync( code ).ConfigureAwait( false ) ??
								throw new UnknownCategoryException( null!, code );

			if( category.Retired is not null )
				throw new ArgumentException( $"Category '{code}' is retired.", nameof(category) );

			return category;
		}

		// A country keeps at least one enabled category (D28).
		private static async Task RequireAnotherEnabledAsync( IDataSession session, string country, string category )
		{
			IEnumerable<CountryCategory> enabled = await session.GetCountryCategoriesAsync( country, false ).ConfigureAwait( false );

			if( !enabled.Any( other => other.Category != category ) )
				throw new CountryConfigurationException( country, $"Country '{country}' must keep at least one enabled category." );
		}

		private static string RequiredCategory( string? category, string parameter ) =>
			OptionalCode( category ) ?? throw new ArgumentException( "A category code is required.", parameter );

		private Task<Category?> ReadCategoryAsync( string code ) =>
			ApplicationDataProvider.ReadAsync(
				async ( session, context ) => await session.GetCategoryAsync( context.code ).ConfigureAwait( false ),
				context: new { code }
			);

		// A write to the global category list, in one transaction; it belongs to no country, so it
		// takes no country lock and bumps no version (D28). Its action timestamp is the time now.
		private async Task<Written<T>> WriteCategoryAsync<T>( Func<IDataSession, ActionContext<Reference>, Task<T>> write )
		{
			ActionContext<Reference> action = Action( DateTime.UtcNow );

			return await ApplicationDataProvider.WriteAsync(
							async ( session, context ) =>
								new Written<T>( await context.write( session, context.action ).ConfigureAwait( false ), context.action ),
							context: new { write, action }
						)
						.ConfigureAwait( false );
		}

		// == Plumbing ==

		private readonly record struct Written<T>( T Value, ActionContext<Reference> Performed )
		{
			public Versioned<T> Versioned() => new() { Value = Value, DatasetVersion = Performed.Timestamp };
		}

		// The principal is known to be present: every write is authorized first.
		private Reference CurrentActor() =>
			new() { Type = "USER", Identifier = SecurityManager.CurrentPrincipal.Identity.Identifier };

		private ActionContext<Reference> Action( DateTime timestamp ) => new() { Actor = CurrentActor(), Timestamp = timestamp };

		// One write to an existing country, in one transaction: takes the country's lock and next
		// version first, then runs write with an action context stamped at that version. An
		// exception from write rolls everything back, version bump included.
		private async Task<Written<T>> WriteAsync<T>( string countryCode, Func<IDataSession, ActionContext<Reference>, Task<T>> write )
		{
			Reference actor = CurrentActor();

			return await ApplicationDataProvider.WriteAsync(
							async ( session, context ) =>
							{
								DateTime version = await session.AdvanceVersionAsync( context.countryCode, DateTime.UtcNow ).ConfigureAwait( false ) ??
													throw new UnknownCountryException( context.countryCode );

								ActionContext<Reference> action = new() { Actor = context.actor, Timestamp = version };

								return new Written<T>( await context.write( session, action ).ConfigureAwait( false ), action );
							},
							context: new { countryCode, write, actor }
						)
						.ConfigureAwait( false );
		}

		// A plain read of the country (null when unknown), then of a value derived from it.
		private Task<T?> ReadCountryValueAsync<T>( string countryCode, Func<IDataSession, Country, Task<T?>> read ) where T : class =>
			ApplicationDataProvider.ReadAsync(
				async ( session, context ) =>
				{
					Country? country = await session.GetCountryAsync( context.countryCode ).ConfigureAwait( false );

					return country is null ? null : await context.read( session, country ).ConfigureAwait( false );
				},
				context: new { countryCode, read }
			);

		private static UnknownLocalityException Unknown( LocalityKey key, string? postcode, string? name ) =>
			new( key.Country, key.Subdivision!, postcode?.Trim()!, name?.Trim()! );

		private static LocalityChange CountryChange( string country, string kind, ActionContext<Reference> performed ) =>
			Change( country, ChangeEntityTypes.Country, country, kind, performed );

		private static LocalityChange SubdivisionChange( string country, string subdivision, string kind, ActionContext<Reference> performed ) =>
			Change( country, ChangeEntityTypes.Subdivision, subdivision, kind, performed );

		private static LocalityChange LocalityChange( LocalityDefinition locality, string kind, ActionContext<Reference> performed ) =>
			Change( locality.Country, ChangeEntityTypes.Locality, locality.Name, kind, performed ) with
			{
				Subdivision = locality.Subdivision, Postcode = locality.Postcode, Category = locality.Category
			};

		private static LocalityChange CountryCategoryChange( string country, string category, string kind, ActionContext<Reference> performed ) =>
			Change( country, ChangeEntityTypes.CountryCategory, category, kind, performed );

		private static LocalityChange Change( string country, string entityType, string key, string kind, ActionContext<Reference> performed ) =>
			new()
			{
				Country = country,
				EntityType = entityType,
				Key = key,
				Kind = kind,
				Performed = performed,
				DatasetVersion = performed.Timestamp
			};

		private Task PublishAsync( LocalityChange change ) =>
			PublishAsync( publisher => publisher.PublishAsync( change ), change.Country );

		// After commit and best-effort: a publisher failure is logged, never thrown (D7).
		private async Task PublishAsync( Func<ILocalityChangeEventPublisher, Task> publish, string country )
		{
			try
			{
				await publish( _eventPublisher ).ConfigureAwait( false );
			}
			catch( Exception exception )
			{
				_logger.LogWarning( exception, "Publishing a committed change to country {Country} failed; the change stands.", country );
			}
		}
	}
}
