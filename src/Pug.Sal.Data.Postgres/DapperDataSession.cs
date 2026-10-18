using System.Data;
using Dapper;
using Npgsql;
using Pug.Application.Data;
using Pug.Effable;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal.Data.Postgres
{
	public class DapperDataSession : ApplicationDataSession, IDataSession
	{
		static DapperDataSession()
		{
			SqlMapper.AddTypeHandler( new JsonTypeHandler<ActionContext<Reference>>() );
		}

		public DapperDataSession( IDbConnection databaseSession ) : base( databaseSession )
		{
		}

		// Npgsql enlists in the ambient TransactionScope itself, so unlike the SQLite store this session
		// needs no enlistment of its own (DECISIONS D15).

		// Flat row shapes of the tables; composed into the sectioned models.
		private sealed class CountryRow
		{
			public string code { get; set; } = null!;
			public string? alpha3 { get; set; }
			public string name { get; set; } = null!;
			public string? postcodePattern { get; set; }
			public bool postcodeRequired { get; set; }
			public bool hasSubdivisions { get; set; }
			public string currentVersion { get; set; } = null!;
			public string? retired { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
		}

		private sealed class SubdivisionRow
		{
			public string country { get; set; } = null!;
			public string code { get; set; } = null!;
			public string name { get; set; } = null!;
			public string? type { get; set; }
			public string? retired { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
		}

		private sealed class CategoryRow
		{
			public string code { get; set; } = null!;
			public string name { get; set; } = null!;
			public string? retired { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
		}

		// name is the global category's, joined on read.
		private sealed class CountryCategoryRow
		{
			public string country { get; set; } = null!;
			public string category { get; set; } = null!;
			public string name { get; set; } = null!;
			public string? disabled { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
		}

		private sealed class LocalityRow
		{
			public string country { get; set; } = null!;
			public string subdivision { get; set; } = null!;
			public string? postcode { get; set; }
			public string name { get; set; } = null!;
			public string? retired { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
			public string category { get; set; } = null!;
		}

		private sealed class ImportRunRow
		{
			public string identifier { get; set; } = null!;
			public string country { get; set; } = null!;
			public string importer { get; set; } = null!;
			public string? source { get; set; }
			public int added { get; set; }
			public int reinstated { get; set; }
			public int deleted { get; set; }
			public int recategorized { get; set; }
			public int unchanged { get; set; }
			public string datasetVersion { get; set; } = null!;
			public ActionContext<Reference> performed { get; set; } = null!;
		}

		private const string CountryColumns =
			@"code, alpha3, name, postcodePattern, postcodeRequired, hasSubdivisions, currentVersion, retired,
				registration, lastUpdate";

		private const string SubdivisionColumns = "country, code, name, type, retired, registration, lastUpdate";

		private const string CategoryColumns = "code, name, retired, registration, lastUpdate";

		// Over countryCategory c joined to its category g.
		private const string CountryCategoryColumns =
			"c.country, c.category, g.name, c.disabled, c.registration, c.lastUpdate";

		private const string LocalityColumns =
			"country, subdivision, postcode, name, retired, registration, lastUpdate, category";

		private const string ImportRunColumns =
			@"identifier, country, importer, source, added, reinstated, deleted, recategorized, unchanged, datasetVersion,
				performed";

		// Npgsql reads timestamptz as DateTimeKind.Utc already; the kind is stamped anyway, as in the
		// SQLite store, so both stores honour the same contract the same way.
		private static DateTime AsUtc( DateTime value ) => DateTime.SpecifyKind( value, DateTimeKind.Utc );

		// retired always equals the action timestamp, a dataset version, so it is stored the same
		// way: DatasetVersions.TextFormat digits, exact to the tick in either store (DECISIONS D19).
		private static string? RetiredText( DateTime? retired ) => retired is null ? null : DatasetVersions.ToText( retired.Value );

		private static DateTime? RetiredFromText( string? text ) => text is null ? null : DatasetVersions.FromText( text );

		// An ActionContext's timestamp is JSON; System.Text.Json yields Utc for a 'Z' timestamp, but
		// the kind is stamped regardless so the contract does not depend on that.
		private static ActionContext<Reference>? AsUtc( ActionContext<Reference>? context ) =>
			context is null ? null : new ActionContext<Reference> { Actor = context.Actor, Timestamp = AsUtc( context.Timestamp ) };

		private static Country Compose( CountryRow row ) =>
			new()
			{
				Definition = new CountryDefinition
				{
					Code = row.code,
					Alpha3 = row.alpha3!,
					Name = row.name,
					PostcodePattern = row.postcodePattern!,
					PostcodeRequired = row.postcodeRequired,
					HasSubdivisions = row.hasSubdivisions
				},
				CurrentVersion = DatasetVersions.FromText( row.currentVersion ),
				Retired = RetiredFromText( row.retired ),
				Registration = AsUtc( row.registration )!,
				LastUpdate = AsUtc( row.lastUpdate )!
			};

		private static Subdivision Compose( SubdivisionRow row ) =>
			new()
			{
				Definition = new SubdivisionDefinition
				{
					Country = row.country,
					Code = row.code,
					Name = row.name,
					Type = row.type!
				},
				Retired = RetiredFromText( row.retired ),
				Registration = AsUtc( row.registration )!,
				LastUpdate = AsUtc( row.lastUpdate )!
			};

		private static Category Compose( CategoryRow row ) =>
			new()
			{
				Definition = new CategoryDefinition { Code = row.code, Name = row.name },
				Retired = RetiredFromText( row.retired ),
				Registration = AsUtc( row.registration )!,
				LastUpdate = AsUtc( row.lastUpdate )!
			};

		private static CountryCategory Compose( CountryCategoryRow row ) =>
			new()
			{
				Country = row.country,
				Category = row.category,
				Name = row.name,
				Disabled = RetiredFromText( row.disabled ),
				Registration = AsUtc( row.registration )!,
				LastUpdate = AsUtc( row.lastUpdate )!
			};

		// '' in the subdivision column means none.
		private static Locality Compose( LocalityRow row ) =>
			new()
			{
				Definition = new LocalityDefinition
				{
					Country = row.country,
					Subdivision = row.subdivision.Length == 0 ? null! : row.subdivision,
					Postcode = row.postcode!,
					Name = row.name,
					Category = row.category
				},
				Retired = RetiredFromText( row.retired ),
				Registration = AsUtc( row.registration )!,
				LastUpdate = AsUtc( row.lastUpdate )!
			};

		private static ImportRun Compose( ImportRunRow row ) =>
			new()
			{
				Identifier = row.identifier,
				Country = row.country,
				Importer = row.importer,
				Source = row.source!,
				Added = row.added,
				Reinstated = row.reinstated,
				Deleted = row.deleted,
				Recategorized = row.recategorized,
				Unchanged = row.unchanged,
				DatasetVersion = DatasetVersions.FromText( row.datasetVersion ),
				Performed = AsUtc( row.performed )!
			};

		// == Countries ==

		// unique_violation (primary keys included); a foreign key violation is left as it is.
		private static bool IsDuplicateKey( PostgresException exception ) =>
			exception.SqlState == PostgresErrorCodes.UniqueViolation;

		private static async Task InsertAsync( Func<Task> insert, string description )
		{
			try
			{
				await insert().ConfigureAwait( false );
			}
			catch( PostgresException exception ) when( IsDuplicateKey( exception ) )
			{
				throw new DuplicateKeyException( $"{description} already exists.", exception );
			}
		}

		public Task InsertCountryAsync( Country country )
		{
			return InsertAsync( () => Connection.ExecuteAsync(
					$@"insert into country({CountryColumns})
						values(@code, @alpha3, @name, @postcodePattern, @postcodeRequired, @hasSubdivisions,
							@currentVersion, @retired, @registration, @lastUpdate);",
					new
					{
						code = country.Definition.Code,
						alpha3 = country.Definition.Alpha3,
						name = country.Definition.Name,
						postcodePattern = country.Definition.PostcodePattern,
						postcodeRequired = country.Definition.PostcodeRequired,
						hasSubdivisions = country.Definition.HasSubdivisions,
						currentVersion = DatasetVersions.ToText( country.CurrentVersion ),
						retired = RetiredText( country.Retired ),
						registration = country.Registration,
						lastUpdate = country.LastUpdate
					}
				), $"Country '{country.Definition.Code}'" );
		}

		public async Task<Country?> GetCountryAsync( string country )
		{
			CountryRow? row = await Connection.QueryFirstOrDefaultAsync<CountryRow?>(
									$"select {CountryColumns} from country where code = @country;",
									new { country }
								).ConfigureAwait( false );

			return row is null ? null : Compose( row );
		}

		public async Task<IEnumerable<Country>> GetCountriesAsync( bool includeRetired )
		{
			IEnumerable<CountryRow> rows = await Connection.QueryAsync<CountryRow>(
													$@"select {CountryColumns} from country
														where @includeRetired or retired is null
														order by code;",
													new { includeRetired }
												).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}

		public async Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync()
		{
			IEnumerable<CountryRow> rows = await Connection.QueryAsync<CountryRow>(
													$"select {CountryColumns} from country order by code;"
												).ConfigureAwait( false );

			return rows.Select( row => new CountryVersion { Country = row.code, Version = DatasetVersions.FromText( row.currentVersion ) } )
						.ToList();
		}

		public async Task<bool> UpdateCountryAsync( CountryDefinition definition, ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									@"update country set
											alpha3 = @alpha3, name = @name, postcodePattern = @postcodePattern,
											postcodeRequired = @postcodeRequired, hasSubdivisions = @hasSubdivisions,
											lastUpdate = @lastUpdate
										where code = @code;",
									new
									{
										code = definition.Code,
										alpha3 = definition.Alpha3,
										name = definition.Name,
										postcodePattern = definition.PostcodePattern,
										postcodeRequired = definition.PostcodeRequired,
										hasSubdivisions = definition.HasSubdivisions,
										lastUpdate
									}
								).ConfigureAwait( false );

			return affected > 0;
		}

		public async Task<bool> SetCountryRetiredAsync( string country, DateTime? retired, ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									"update country set retired = @retired, lastUpdate = @lastUpdate where code = @country;",
									new { country, retired = RetiredText( retired ), lastUpdate }
								).ConfigureAwait( false );

			return affected > 0;
		}

		public async Task<DateTime?> AdvanceVersionAsync( string country, DateTime utcNow )
		{
			// The row lock is taken as the version is read, so concurrent callers are serialized
			// and never read a stale version.
			string? current = await Connection.QuerySingleOrDefaultAsync<string?>(
										"select currentVersion from country where code = @country for update;",
										new { country }
									).ConfigureAwait( false );

			if( current is null )
				return null;

			DateTime next = DatasetVersions.Next( DatasetVersions.FromText( current ), utcNow );

			await Connection.ExecuteAsync(
						"update country set currentVersion = @next where code = @country;",
						new { country, next = DatasetVersions.ToText( next ) }
					).ConfigureAwait( false );

			return next;
		}

		// == Global categories ==

		public Task InsertCategoryAsync( Category category )
		{
			return InsertAsync( () => Connection.ExecuteAsync(
					$@"insert into category({CategoryColumns})
						values(@code, @name, @retired, @registration, @lastUpdate);",
					new
					{
						code = category.Definition.Code,
						name = category.Definition.Name,
						retired = RetiredText( category.Retired ),
						registration = category.Registration,
						lastUpdate = category.LastUpdate
					}
				), $"Category '{category.Definition.Code}'" );
		}

		public async Task<Category?> GetCategoryAsync( string category )
		{
			CategoryRow? row = await Connection.QueryFirstOrDefaultAsync<CategoryRow?>(
									$"select {CategoryColumns} from category where code = @category;",
									new { category }
								).ConfigureAwait( false );

			return row is null ? null : Compose( row );
		}

		public async Task<IEnumerable<Category>> GetCategoriesAsync( bool includeRetired )
		{
			IEnumerable<CategoryRow> rows = await Connection.QueryAsync<CategoryRow>(
													$@"select {CategoryColumns} from category
														where @includeRetired or retired is null
														order by code;",
													new { includeRetired }
												).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}

		public async Task<bool> UpdateCategoryAsync( CategoryDefinition definition, ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									"update category set name = @name, lastUpdate = @lastUpdate where code = @code;",
									new { code = definition.Code, name = definition.Name, lastUpdate }
								).ConfigureAwait( false );

			return affected > 0;
		}

		public async Task<bool> SetCategoryRetiredAsync( string category, DateTime? retired, ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									"update category set retired = @retired, lastUpdate = @lastUpdate where code = @category;",
									new { category, retired = RetiredText( retired ), lastUpdate }
								).ConfigureAwait( false );

			return affected > 0;
		}

		// == Country categories ==

		public Task InsertCountryCategoryAsync( CountryCategory category )
		{
			return InsertAsync( () => Connection.ExecuteAsync(
					@"insert into countryCategory(country, category, disabled, registration, lastUpdate)
						values(@country, @category, @disabled, @registration, @lastUpdate);",
					new
					{
						country = category.Country,
						category = category.Category,
						disabled = RetiredText( category.Disabled ),
						registration = category.Registration,
						lastUpdate = category.LastUpdate
					}
				), $"Category '{category.Category}' of country '{category.Country}'" );
		}

		public async Task<CountryCategory?> GetCountryCategoryAsync( string country, string category )
		{
			CountryCategoryRow? row = await Connection.QueryFirstOrDefaultAsync<CountryCategoryRow?>(
											$@"select {CountryCategoryColumns}
												from countryCategory c join category g on g.code = c.category
												where c.country = @country and c.category = @category;",
											new { country, category }
										).ConfigureAwait( false );

			return row is null ? null : Compose( row );
		}

		public async Task<IEnumerable<CountryCategory>> GetCountryCategoriesAsync( string country, bool includeDisabled )
		{
			IEnumerable<CountryCategoryRow> rows = await Connection.QueryAsync<CountryCategoryRow>(
														$@"select {CountryCategoryColumns}
															from countryCategory c join category g on g.code = c.category
															where c.country = @country and ( @includeDisabled or c.disabled is null )
															order by c.category;",
														new { country, includeDisabled }
													).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}

		public async Task<bool> SetCountryCategoryDisabledAsync( string country, string category, DateTime? disabled,
																ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									@"update countryCategory set disabled = @disabled, lastUpdate = @lastUpdate
										where country = @country and category = @category;",
									new { country, category, disabled = RetiredText( disabled ), lastUpdate }
								).ConfigureAwait( false );

			return affected > 0;
		}

		public async Task<bool> DeleteCountryCategoryAsync( string country, string category )
		{
			int affected = await Connection.ExecuteAsync(
									"delete from countryCategory where country = @country and category = @category;",
									new { country, category }
								).ConfigureAwait( false );

			return affected > 0;
		}

		// == Subdivisions ==

		public Task InsertSubdivisionAsync( Subdivision subdivision )
		{
			return InsertAsync( () => Connection.ExecuteAsync(
					$@"insert into subdivision({SubdivisionColumns})
						values(@country, @code, @name, @type, @retired, @registration, @lastUpdate);",
					new
					{
						country = subdivision.Definition.Country,
						code = subdivision.Definition.Code,
						name = subdivision.Definition.Name,
						type = subdivision.Definition.Type,
						retired = RetiredText( subdivision.Retired ),
						registration = subdivision.Registration,
						lastUpdate = subdivision.LastUpdate
					}
				), $"Subdivision '{subdivision.Definition.Country}/{subdivision.Definition.Code}'" );
		}

		public async Task<Subdivision?> GetSubdivisionAsync( string country, string subdivision )
		{
			SubdivisionRow? row = await Connection.QueryFirstOrDefaultAsync<SubdivisionRow?>(
									$"select {SubdivisionColumns} from subdivision where country = @country and code = @subdivision;",
									new { country, subdivision }
								).ConfigureAwait( false );

			return row is null ? null : Compose( row );
		}

		public async Task<IEnumerable<Subdivision>> GetSubdivisionsAsync( string country, bool includeRetired )
		{
			IEnumerable<SubdivisionRow> rows = await Connection.QueryAsync<SubdivisionRow>(
													$@"select {SubdivisionColumns} from subdivision
														where country = @country and ( @includeRetired or retired is null )
														order by code;",
													new { country, includeRetired }
												).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}

		public async Task<bool> UpdateSubdivisionAsync( SubdivisionDefinition definition, ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									@"update subdivision set name = @name, type = @type, lastUpdate = @lastUpdate
										where country = @country and code = @code;",
									new
									{
										country = definition.Country,
										code = definition.Code,
										name = definition.Name,
										type = definition.Type,
										lastUpdate
									}
								).ConfigureAwait( false );

			return affected > 0;
		}

		public async Task<bool> SetSubdivisionRetiredAsync( string country, string subdivision, DateTime? retired, ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									@"update subdivision set retired = @retired, lastUpdate = @lastUpdate
										where country = @country and code = @subdivision;",
									new { country, subdivision, retired = RetiredText( retired ), lastUpdate }
								).ConfigureAwait( false );

			return affected > 0;
		}

		// == Localities ==

		public Task InsertLocalityAsync( Locality locality, string? postcodeKey, string nameKey )
		{
			return InsertAsync( () => Connection.ExecuteAsync(
					$@"insert into locality({LocalityColumns}, postcodeKey, nameKey, stamped)
						values(@country, @subdivision, @postcode, @name, @retired, @registration, @lastUpdate, @category,
							@postcodeKey, @nameKey, @stamped);",
					InsertParameters( locality, postcodeKey, nameKey )
				), $"Locality '{locality.Definition.Country}/{locality.Definition.Subdivision}/{postcodeKey}/{nameKey}'" );
		}

		private static object InsertParameters( Locality locality, string? postcodeKey, string nameKey ) =>
			new
			{
				country = locality.Definition.Country,
				subdivision = locality.Definition.Subdivision ?? string.Empty,
				postcode = locality.Definition.Postcode,
				name = locality.Definition.Name,
				category = locality.Definition.Category,
				retired = RetiredText( locality.Retired ),
				registration = locality.Registration,
				lastUpdate = locality.LastUpdate,
				postcodeKey = postcodeKey ?? string.Empty,
				nameKey,
				stamped = DatasetVersions.ToText( locality.Registration.Timestamp )
			};

		public async Task<Locality?> GetLocalityAsync( LocalityKey key )
		{
			LocalityRow? row = await Connection.QueryFirstOrDefaultAsync<LocalityRow?>(
									$@"select {LocalityColumns} from locality
										where country = @country and subdivision = @subdivision
											and postcodeKey = @postcodeKey and nameKey = @nameKey;",
									KeyParameters( key )
								).ConfigureAwait( false );

			return row is null ? null : Compose( row );
		}

		private static object KeyParameters( LocalityKey key ) =>
			new
			{
				country = key.Country,
				subdivision = key.Subdivision ?? string.Empty,
				postcodeKey = key.PostcodeKey ?? string.Empty,
				nameKey = key.NameKey,
			};

		public async Task<IEnumerable<Locality>> GetLocalitiesAsync( string country, string? subdivision, string? postcodeKey, string? nameKey,
																	string? category, bool includeRetired )
		{
			string filter = "country = @country";

			if( subdivision is not null )
				filter += " and subdivision = @subdivision";

			if( postcodeKey is not null )
				filter += " and postcodeKey = @postcodeKey";

			if( nameKey is not null )
				filter += " and nameKey = @nameKey";

			if( category is not null )
				filter += " and category = @category";

			if( !includeRetired )
				filter += " and retired is null";

			IEnumerable<LocalityRow> rows = await Connection.QueryAsync<LocalityRow>(
													$@"select {LocalityColumns} from locality where {filter}
														order by subdivision, nameKey, postcodeKey;",
													new { country, subdivision, postcodeKey, nameKey, category }
												).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}

		public async Task<IEnumerable<Locality>> SearchLocalitiesAsync( string country, string namePrefixKey, string? subdivision,
																		string? category, int limit )
		{
			// Prefix matches first, in name order; then, when there is room, close non-prefix matches
			// by pg_trgm similarity (the '%' operator, at pg_trgm.similarity_threshold), best first.
			// The prefix is compared with left() rather than LIKE, so '%', '_' and '\' in it are
			// taken literally; the range bound lets the (country, nameKey) index narrow the scan.
			string subdivisionFilter = ( subdivision is null ? string.Empty : " and subdivision = @subdivision" ) +
										( category is null ? string.Empty : " and category = @category" );

			IEnumerable<LocalityRow> rows = await Connection.QueryAsync<LocalityRow>(
													$@"select {LocalityColumns} from (
															( select {LocalityColumns}, nameKey, postcodeKey, 0 as tier, 0::real as score
															from locality
															where country = @country and retired is null{subdivisionFilter}
																and nameKey >= @prefix and left( nameKey, length( @prefix ) ) = @prefix
															order by nameKey, subdivision, postcodeKey
															limit @limit )
															union all
															( select {LocalityColumns}, nameKey, postcodeKey, 1 as tier, similarity( nameKey, @prefix ) as score
															from locality
															where country = @country and retired is null{subdivisionFilter}
																and nameKey % @prefix and left( nameKey, length( @prefix ) ) <> @prefix
															order by score desc, nameKey, subdivision, postcodeKey
															limit @limit )
														) matched
														order by tier, score desc, nameKey, subdivision, postcodeKey
														limit @limit;",
													new { country, prefix = namePrefixKey, subdivision, category, limit }
												).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}

		public async Task<bool> SetLocalityRetiredAsync( LocalityKey key, DateTime? retired, ActionContext<Reference> lastUpdate )
		{
			int affected = await Connection.ExecuteAsync(
									@"update locality set retired = @retired, lastUpdate = @lastUpdate, stamped = @stamped
										where country = @country and subdivision = @subdivision
											and postcodeKey = @postcodeKey and nameKey = @nameKey;",
									new
									{
										country = key.Country,
										subdivision = key.Subdivision ?? string.Empty,
										postcodeKey = key.PostcodeKey ?? string.Empty,
										nameKey = key.NameKey,
										retired = RetiredText( retired ),
										lastUpdate,
										stamped = DatasetVersions.ToText( lastUpdate.Timestamp )
									}
								).ConfigureAwait( false );

			return affected > 0;
		}

		public async Task<bool> DeleteLocalityAsync( LocalityKey key )
		{
			int affected = await Connection.ExecuteAsync(
									@"delete from locality
										where country = @country and subdivision = @subdivision
											and postcodeKey = @postcodeKey and nameKey = @nameKey;",
									KeyParameters( key )
								).ConfigureAwait( false );

			return affected > 0;
		}

		public Task<bool> HasLocalitiesAsync( string country, string? category = null )
		{
			return Connection.ExecuteScalarAsync<bool>(
					category is null
						? "select exists( select 1 from locality where country = @country );"
						: "select exists( select 1 from locality where country = @country and category = @category );",
					new { country, category }
				);
		}

		public Task<int> ReplaceLocalityCategoryAsync( string country, string from, string to, ActionContext<Reference> lastUpdate )
		{
			return Connection.ExecuteAsync(
					@"update locality set category = @to, lastUpdate = @lastUpdate, stamped = @stamped
						where country = @country and category = @from;",
					new { country, from, to, lastUpdate, stamped = DatasetVersions.ToText( lastUpdate.Timestamp ) }
				);
		}

		// == Imports ==

		public async Task<LocalityUpsertOutcome> UpsertImportedLocalityAsync( Locality locality, string? postcodeKey, string nameKey, ActionContext<Reference> context )
		{
			LocalityKey key = new( locality.Definition.Country, locality.Definition.Subdivision, postcodeKey, nameKey );

			// The retired timestamp is selected as a flag only; a missing row yields no result.
			IEnumerable<(bool Retired, string Category)> existing = await Connection.QueryAsync<(bool, string)>(
																		@"select retired is not null, category from locality
																			where country = @country and subdivision = @subdivision
																				and postcodeKey = @postcodeKey and nameKey = @nameKey;",
																		KeyParameters( key )
																	).ConfigureAwait( false );

			(bool Retired, string Category)[] found = existing.ToArray();

			if( found.Length == 0 )
			{
				await InsertLocalityAsync(
							locality with { Retired = null, Registration = context, LastUpdate = null! },
							postcodeKey, nameKey
						).ConfigureAwait( false );

				return LocalityUpsertOutcome.Added;
			}

			await Connection.ExecuteAsync(
						@"update locality set retired = null, category = @category, lastUpdate = @lastUpdate, stamped = @stamped
							where country = @country and subdivision = @subdivision
								and postcodeKey = @postcodeKey and nameKey = @nameKey;",
						new
						{
							category = locality.Definition.Category,
							country = key.Country,
							subdivision = key.Subdivision ?? string.Empty,
							postcodeKey = key.PostcodeKey ?? string.Empty,
							nameKey = key.NameKey,
							lastUpdate = context,
							stamped = DatasetVersions.ToText( context.Timestamp )
						}
					).ConfigureAwait( false );

			return found[0].Retired ? LocalityUpsertOutcome.Reinstated :
					found[0].Category != locality.Definition.Category ? LocalityUpsertOutcome.Recategorized :
					LocalityUpsertOutcome.Unchanged;
		}

		public Task<int> DeleteLocalitiesStampedBeforeAsync( string country, DateTime timestamp )
		{
			return Connection.ExecuteAsync(
					"delete from locality where country = @country and stamped < @timestamp;",
					new { country, timestamp = DatasetVersions.ToText( timestamp ) }
				);
		}

		public Task InsertImportRunAsync( ImportRun run )
		{
			return Connection.ExecuteAsync(
					$@"insert into importRun({ImportRunColumns})
						values(@identifier, @country, @importer, @source, @added, @reinstated, @deleted,
							@recategorized, @unchanged, @datasetVersion, @performed);",
					new
					{
						identifier = run.Identifier,
						country = run.Country,
						importer = run.Importer,
						source = run.Source,
						added = run.Added,
						reinstated = run.Reinstated,
						deleted = run.Deleted,
						recategorized = run.Recategorized,
						unchanged = run.Unchanged,
						datasetVersion = DatasetVersions.ToText( run.DatasetVersion ),
						performed = run.Performed
					}
				);
		}

		public async Task<IEnumerable<ImportRun>> GetImportRunsAsync( string country )
		{
			IEnumerable<ImportRunRow> rows = await Connection.QueryAsync<ImportRunRow>(
													$@"select {ImportRunColumns} from importRun where country = @country
														order by datasetVersion desc;",
													new { country }
												).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}
	}
}
