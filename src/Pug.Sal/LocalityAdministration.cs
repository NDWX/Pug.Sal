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

		private readonly ILocalityChangePublisher _publisher;

		private readonly ILogger<LocalityAdministration> _logger;

		private readonly ImportCoordinator _coordinator;

		public LocalityAdministration( IApplicationData<IDataSession> applicationData,
										ISecurityManager securityManager,
										INameNormalizer normalizer,
										IDatasetImporterRegistry importers,
										ILocalityChangePublisher publisher,
										ILogger<LocalityAdministration>? logger = null )
			: base( applicationData, securityManager, normalizer )
		{
			_importers = importers ?? throw new ArgumentNullException( nameof(importers) );
			_publisher = publisher ?? throw new ArgumentNullException( nameof(publisher) );
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

		public Task<Versioned<IEnumerable<Locality>>> GetLocalitiesAsync( string country, string subdivision = null!,
																		string postcode = null!, string name = null!,
																		bool includeRetired = false ) =>
			ReadLocalitiesAsync( country, subdivision, postcode, name, includeRetired );

		public Task<Versioned<Locality>> GetLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			ReadLocalityAsync( country, subdivision, postcode, name );

		public Task<Versioned<IEnumerable<Locality>>> SearchLocalitiesAsync( string country, string text, string subdivision = null!,
																			int limit = 20 ) =>
			SearchAsync( country, text, subdivision, limit );

		// == Countries ==

		public async Task<Country> CreateCountryAsync( CountryDefinition definition )
		{
			await CheckAuthorizationAsync( SecurityOperations.Create ).ConfigureAwait( false );

			CountryDefinition stored = DefinitionRules.Country( definition );

			// A new country has no lock to take: its first version is simply the next after none.
			ActionContext<Reference> action = Action( DatasetVersions.Next( DateTime.MinValue, DateTime.UtcNow ) );
			Country country = new() { Definition = stored, CurrentVersion = action.Timestamp, Registration = action };

			try
			{
				await ApplicationDataProvider.WriteAsync(
												async ( session, context ) =>
												{
													await session.InsertCountryAsync( context.country ).ConfigureAwait( false );

													return true;
												},
												context: new { country }
											)
											.ConfigureAwait( false );
			}
			catch( DuplicateKeyException )
			{
				throw new DuplicateEntryException( ChangeEntityTypes.Country, stored.Code );
			}

			await PublishAsync( CountryChange( stored.Code, ChangeKinds.Created, action ) ).ConfigureAwait( false );

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
			Change( country, ChangeEntityTypes.Country, country, kind, performed, null, null );

		private static LocalityChange SubdivisionChange( string country, string subdivision, string kind, ActionContext<Reference> performed ) =>
			Change( country, ChangeEntityTypes.Subdivision, subdivision, kind, performed, null, null );

		private static LocalityChange LocalityChange( LocalityDefinition locality, string kind, ActionContext<Reference> performed ) =>
			Change( locality.Country, ChangeEntityTypes.Locality, locality.Name, kind, performed, locality.Subdivision, locality.Postcode );

		private static LocalityChange Change( string country, string entityType, string key, string kind, ActionContext<Reference> performed,
											string? subdivision, string? postcode ) =>
			new()
			{
				Country = country,
				EntityType = entityType,
				Subdivision = subdivision!,
				Postcode = postcode!,
				Key = key,
				Kind = kind,
				Performed = performed,
				DatasetVersion = performed.Timestamp
			};

		private Task PublishAsync( LocalityChange change ) =>
			PublishAsync( publisher => publisher.PublishAsync( change ), change.Country );

		// After commit and best-effort: a publisher failure is logged, never thrown (D7).
		private async Task PublishAsync( Func<ILocalityChangePublisher, Task> publish, string country )
		{
			try
			{
				await publish( _publisher ).ConfigureAwait( false );
			}
			catch( Exception exception )
			{
				_logger.LogWarning( exception, "Publishing a committed change to country {Country} failed; the change stands.", country );
			}
		}
	}
}
