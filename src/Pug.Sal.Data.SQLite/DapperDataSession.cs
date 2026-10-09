using System.Data;
using System.Transactions;
using Dapper;
using Pug.Application.Data;
using Pug.Effable;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal.Data.SQLite
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

		// Microsoft.Data.Sqlite ignores an ambient System.Transactions transaction: statements would
		// autocommit, giving ApplicationData's TransactionScope-based ExecuteAsync/PerformAsync no
		// atomicity and no write lock held across a unit of work (which AdvanceVersionAsync relies
		// on). So the session opens a real SQLite transaction when created inside a scope and ties
		// its commit/rollback to the scope's outcome.
		private ScopeEnlistment? _enlistment;

		internal void EnlistInAmbientTransaction( Transaction transaction )
		{
			BeginTransaction();
			_enlistment = new ScopeEnlistment( this );
			transaction.EnlistVolatile( _enlistment, EnlistmentOptions.None );
		}

		// ApplicationData disposes the session before the scope completes, so while an enlisted
		// transaction is still pending, closing the connection is left to the enlistment.
		public override void Dispose()
		{
			if( _enlistment is not null && !_enlistment.Completed )
			{
				_enlistment.DisposeRequested = true;
				return;
			}

			base.Dispose();
		}

		private sealed class ScopeEnlistment : IEnlistmentNotification
		{
			private readonly DapperDataSession _session;

			public ScopeEnlistment( DapperDataSession session )
			{
				_session = session;
			}

			public bool Completed { get; private set; }

			public bool DisposeRequested { get; set; }

			private bool _committed;

			// The SQLite transaction is committed here, in phase one, not in Commit: a commit that
			// fails (e.g. SQLITE_BUSY) must abort the scope so its Dispose throws, rather than surface
			// after the scope has already reported success. Committing in phase one cannot be undone
			// if another participant of the same scope then fails; Sal's units of work enlist nothing
			// else.
			public void Prepare( PreparingEnlistment preparingEnlistment )
			{
				try
				{
					_session.CommitTransaction();
				}
				catch( Exception exception )
				{
					try
					{
						_session.RollbackTransaction();
					}
					catch
					{
						// The commit failure is the error worth reporting.
					}

					Release();
					preparingEnlistment.ForceRollback( exception );

					return;
				}

				_committed = true;
				preparingEnlistment.Prepared();
			}

			public void Commit( Enlistment enlistment ) => Finish( enlistment );

			public void Rollback( Enlistment enlistment ) => Abandon( enlistment );

			public void InDoubt( Enlistment enlistment ) => Abandon( enlistment );

			private void Abandon( Enlistment enlistment )
			{
				try
				{
					if( !_committed )
						_session.RollbackTransaction();
				}
				finally
				{
					Finish( enlistment );
				}
			}

			private void Finish( Enlistment enlistment )
			{
				try
				{
					Release();
				}
				finally
				{
					enlistment.Done();
				}
			}

			private void Release()
			{
				if( Completed )
					return;

				Completed = true;

				if( DisposeRequested )
					_session.Dispose();
			}
		}

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
			public DateTime? retired { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
		}

		private sealed class SubdivisionRow
		{
			public string country { get; set; } = null!;
			public string code { get; set; } = null!;
			public string name { get; set; } = null!;
			public string? type { get; set; }
			public DateTime? retired { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
		}

		private sealed class LocalityRow
		{
			public string country { get; set; } = null!;
			public string subdivision { get; set; } = null!;
			public string? postcode { get; set; }
			public string name { get; set; } = null!;
			public DateTime? retired { get; set; }
			public ActionContext<Reference> registration { get; set; } = null!;
			public ActionContext<Reference>? lastUpdate { get; set; }
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
			public int unchanged { get; set; }
			public string datasetVersion { get; set; } = null!;
			public ActionContext<Reference> performed { get; set; } = null!;
		}

		private const string CountryColumns =
			@"code, alpha3, name, postcodePattern, postcodeRequired, hasSubdivisions, currentVersion, retired,
				registration, lastUpdate";

		private const string SubdivisionColumns = "country, code, name, type, retired, registration, lastUpdate";

		private const string LocalityColumns =
			"country, subdivision, postcode, name, retired, registration, lastUpdate";

		private const string ImportRunColumns =
			@"identifier, country, importer, source, added, reinstated, deleted, unchanged, datasetVersion, performed";

		// SQLite stores timestamps as text without an offset, so the provider hands them back as
		// DateTimeKind.Unspecified. Every time in this domain is UTC (and the Postgres provider's
		// timestamptz columns say so), so the kind is stamped on read.
		private static DateTime AsUtc( DateTime value ) => DateTime.SpecifyKind( value, DateTimeKind.Utc );

		private static DateTime? AsUtc( DateTime? value ) => value is null ? null : AsUtc( value.Value );

		// An ActionContext's timestamp is JSON; System.Text.Json yields Utc for a 'Z' timestamp, but
		// the kind is stamped regardless so the contract does not depend on that.
		private static ActionContext<Reference>? AsUtc( ActionContext<Reference>? context ) =>
			context is null ? null : new ActionContext<Reference> { Actor = context.Actor, Timestamp = AsUtc( context.Timestamp ) };

		private static CountryInfo Compose( CountryRow row ) =>
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
				Retired = AsUtc( row.retired ),
				Registration = AsUtc( row.registration )!,
				LastUpdate = AsUtc( row.lastUpdate )!
			};

		private static SubdivisionInfo Compose( SubdivisionRow row ) =>
			new()
			{
				Definition = new SubdivisionDefinition
				{
					Country = row.country,
					Code = row.code,
					Name = row.name,
					Type = row.type!
				},
				Retired = AsUtc( row.retired ),
				Registration = AsUtc( row.registration )!,
				LastUpdate = AsUtc( row.lastUpdate )!
			};

		// '' in the subdivision column means none.
		private static LocalityInfo Compose( LocalityRow row ) =>
			new()
			{
				Definition = new LocalityDefinition
				{
					Country = row.country,
					Subdivision = row.subdivision.Length == 0 ? null! : row.subdivision,
					Postcode = row.postcode!,
					Name = row.name
				},
				Retired = AsUtc( row.retired ),
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
				Unchanged = row.unchanged,
				DatasetVersion = DatasetVersions.FromText( row.datasetVersion ),
				Performed = AsUtc( row.performed )!
			};

		// == Countries ==

		public Task InsertCountryAsync( CountryInfo country )
		{
			return Connection.ExecuteAsync(
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
						retired = country.Retired,
						registration = country.Registration,
						lastUpdate = country.LastUpdate
					}
				);
		}

		public async Task<CountryInfo?> GetCountryAsync( string country )
		{
			CountryRow? row = await Connection.QueryFirstOrDefaultAsync<CountryRow?>(
									$"select {CountryColumns} from country where code = @country;",
									new { country }
								).ConfigureAwait( false );

			return row is null ? null : Compose( row );
		}

		public async Task<IEnumerable<CountryInfo>> GetCountriesAsync( bool includeRetired )
		{
			IEnumerable<CountryRow> rows = await Connection.QueryAsync<CountryRow>(
													$@"select {CountryColumns} from country
														where @includeRetired = 1 or retired is null
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
									new { country, retired, lastUpdate }
								).ConfigureAwait( false );

			return affected > 0;
		}

		public async Task<DateTime?> AdvanceVersionAsync( string country, DateTime utcNow )
		{
			// A no-op update that takes the write lock (in SQLite, the database's) before the
			// version is read, so concurrent callers are serialized and never read a stale version.
			int affected = await Connection.ExecuteAsync(
									"update country set currentVersion = currentVersion where code = @country;",
									new { country }
								).ConfigureAwait( false );

			if( affected == 0 )
				return null;

			string current = await Connection.QuerySingleAsync<string>(
										"select currentVersion from country where code = @country;",
										new { country }
									).ConfigureAwait( false );

			DateTime next = DatasetVersions.Next( DatasetVersions.FromText( current ), utcNow );

			await Connection.ExecuteAsync(
						"update country set currentVersion = @next where code = @country;",
						new { country, next = DatasetVersions.ToText( next ) }
					).ConfigureAwait( false );

			return next;
		}

		// == Subdivisions ==

		public Task InsertSubdivisionAsync( SubdivisionInfo subdivision )
		{
			return Connection.ExecuteAsync(
					$@"insert into subdivision({SubdivisionColumns})
						values(@country, @code, @name, @type, @retired, @registration, @lastUpdate);",
					new
					{
						country = subdivision.Definition.Country,
						code = subdivision.Definition.Code,
						name = subdivision.Definition.Name,
						type = subdivision.Definition.Type,
						retired = subdivision.Retired,
						registration = subdivision.Registration,
						lastUpdate = subdivision.LastUpdate
					}
				);
		}

		public async Task<SubdivisionInfo?> GetSubdivisionAsync( string country, string subdivision )
		{
			SubdivisionRow? row = await Connection.QueryFirstOrDefaultAsync<SubdivisionRow?>(
									$"select {SubdivisionColumns} from subdivision where country = @country and code = @subdivision;",
									new { country, subdivision }
								).ConfigureAwait( false );

			return row is null ? null : Compose( row );
		}

		public async Task<IEnumerable<SubdivisionInfo>> GetSubdivisionsAsync( string country, bool includeRetired )
		{
			IEnumerable<SubdivisionRow> rows = await Connection.QueryAsync<SubdivisionRow>(
													$@"select {SubdivisionColumns} from subdivision
														where country = @country and ( @includeRetired = 1 or retired is null )
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
									new { country, subdivision, retired, lastUpdate }
								).ConfigureAwait( false );

			return affected > 0;
		}

		// == Localities ==

		public Task InsertLocalityAsync( LocalityInfo locality, string? postcodeKey, string nameKey )
		{
			return Connection.ExecuteAsync(
					$@"insert into locality({LocalityColumns}, postcodeKey, nameKey, stamped)
						values(@country, @subdivision, @postcode, @name, @retired, @registration, @lastUpdate,
							@postcodeKey, @nameKey, @stamped);",
					InsertParameters( locality, postcodeKey, nameKey )
				);
		}

		private static object InsertParameters( LocalityInfo locality, string? postcodeKey, string nameKey ) =>
			new
			{
				country = locality.Definition.Country,
				subdivision = locality.Definition.Subdivision ?? string.Empty,
				postcode = locality.Definition.Postcode,
				name = locality.Definition.Name,
				retired = locality.Retired,
				registration = locality.Registration,
				lastUpdate = locality.LastUpdate,
				postcodeKey = postcodeKey ?? string.Empty,
				nameKey,
				stamped = DatasetVersions.ToText( locality.Registration.Timestamp )
			};

		public async Task<LocalityInfo?> GetLocalityAsync( LocalityKey key )
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

		public async Task<IEnumerable<LocalityInfo>> GetLocalitiesAsync( string country, string? subdivision, string? postcodeKey, string? nameKey, bool includeRetired )
		{
			string filter = "country = @country";

			if( subdivision is not null )
				filter += " and subdivision = @subdivision";

			if( postcodeKey is not null )
				filter += " and postcodeKey = @postcodeKey";

			if( nameKey is not null )
				filter += " and nameKey = @nameKey";

			if( !includeRetired )
				filter += " and retired is null";

			IEnumerable<LocalityRow> rows = await Connection.QueryAsync<LocalityRow>(
													$@"select {LocalityColumns} from locality where {filter}
														order by subdivision, nameKey, postcodeKey;",
													new { country, subdivision, postcodeKey, nameKey }
												).ConfigureAwait( false );

			return rows.Select( Compose ).ToList();
		}

		public async Task<IEnumerable<LocalityInfo>> SearchLocalitiesAsync( string country, string namePrefixKey, string? subdivision, int limit )
		{
			// A substr comparison rather than LIKE, so '%', '_' and any escape character in the
			// prefix are taken literally (and the match is case-sensitive, like Postgres LIKE). The
			// range bound lets the (country, nameKey) index narrow the scan.
			string filter = @"country = @country and retired is null
								and nameKey >= @prefix and substr( nameKey, 1, length( @prefix ) ) = @prefix";

			if( subdivision is not null )
				filter += " and subdivision = @subdivision";

			IEnumerable<LocalityRow> rows = await Connection.QueryAsync<LocalityRow>(
													$@"select {LocalityColumns} from locality where {filter}
														order by nameKey, subdivision, postcodeKey
														limit @limit;",
													new { country, prefix = namePrefixKey, subdivision, limit }
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
										retired,
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

		// == Imports ==

		public async Task<LocalityUpsertOutcome> UpsertImportedLocalityAsync( LocalityInfo locality, string? postcodeKey, string nameKey, ActionContext<Reference> context )
		{
			LocalityKey key = new( locality.Definition.Country, locality.Definition.Subdivision, postcodeKey, nameKey );

			// The retired timestamp is selected as a flag only; a missing row yields no result.
			IEnumerable<bool> existing = await Connection.QueryAsync<bool>(
												@"select retired is not null from locality
													where country = @country and subdivision = @subdivision
														and postcodeKey = @postcodeKey and nameKey = @nameKey;",
												KeyParameters( key )
											).ConfigureAwait( false );

			bool[] found = existing.ToArray();

			if( found.Length == 0 )
			{
				await InsertLocalityAsync(
							locality with { Retired = null, Registration = context, LastUpdate = null! },
							postcodeKey, nameKey
						).ConfigureAwait( false );

				return LocalityUpsertOutcome.Added;
			}

			await Connection.ExecuteAsync(
						@"update locality set retired = null, lastUpdate = @lastUpdate, stamped = @stamped
							where country = @country and subdivision = @subdivision
								and postcodeKey = @postcodeKey and nameKey = @nameKey;",
						new
						{
							country = key.Country,
							subdivision = key.Subdivision ?? string.Empty,
							postcodeKey = key.PostcodeKey ?? string.Empty,
							nameKey = key.NameKey,
							lastUpdate = context,
							stamped = DatasetVersions.ToText( context.Timestamp )
						}
					).ConfigureAwait( false );

			return found[0] ? LocalityUpsertOutcome.Reinstated : LocalityUpsertOutcome.Unchanged;
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
							@unchanged, @datasetVersion, @performed);",
					new
					{
						identifier = run.Identifier,
						country = run.Country,
						importer = run.Importer,
						source = run.Source,
						added = run.Added,
						reinstated = run.Reinstated,
						deleted = run.Deleted,
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
