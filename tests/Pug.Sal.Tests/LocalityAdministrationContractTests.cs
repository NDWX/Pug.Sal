using System.Text;
using Pug.Application.Security;
using Pug.Sal.Abstractions;
using Pug.Sal.Importers.Csv;
using Pug.Sal.Models;
using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

// ILocalityAdministration over a real store, run against every store: SqliteLocalityAdministrationTests
// here, PostgresLocalityAdministrationTests in Pug.Sal.Postgres.Tests.
public abstract class LocalityAdministrationContractTests<TFixture> : IClassFixture<TFixture>, IAsyncLifetime
	where TFixture : class, IStoreFixture
{
	private readonly TFixture _fixture;

	private readonly RecordingEventPublisher _eventPublisher = new();

	private readonly LocalityAdministration _admin;

	protected LocalityAdministrationContractTests( TFixture fixture )
	{
		_fixture = fixture;
		_admin = Admin();
	}

	// The global categories every test's countries are given (DECISIONS D28): TOWN and SUBURB.
	// Tests share the fixture's database, so they are created once.
	public async Task InitializeAsync()
	{
		foreach( ( string code, string name ) in new[] { ( "TOWN", "Town" ), ( "SUBURB", "Suburb" ) } )
		{
			try
			{
				await _admin.CreateCategoryAsync( new CategoryDefinition { Code = code, Name = name } );
			}
			catch( DuplicateEntryException )
			{
			}
		}
	}

	public Task DisposeAsync() => Task.CompletedTask;

	// Each test uses its own country codes, since tests share the fixture's database.
	private static int _countryCounter;

	private static string NewCountry()
	{
		int n = Interlocked.Increment( ref _countryCounter );
		return $"{(char)( 'A' + n / 26 % 26 )}{(char)( 'A' + n % 26 )}";
	}

	private LocalityAdministration Admin( ISecurityManager? security = null, ILocalityChangeEventPublisher? publisher = null ) =>
		new( _fixture.ApplicationData,
			security ?? SecurityManagers.Authorized( "alice" ),
			new NameNormalizer(),
			new DatasetImporterRegistry( new[] { KeyValuePair.Create<string, IDatasetImporter>( "csv", new CsvDatasetImporter() ) } ),
			publisher ?? _eventPublisher );

	private static CountryDefinition AustraliaLike( string code ) =>
		new()
		{
			Code = code,
			Alpha3 = code + "X",
			Name = "Country " + code,
			PostcodePattern = @"^\d{4}$",
			PostcodeRequired = true,
			HasSubdivisions = true,
			Categories = new[] { "TOWN", "SUBURB" }
		};

	private static LocalityDefinition LocalityOf( string country, string? subdivision, string? postcode, string name, string category = "TOWN" ) =>
		new() { Country = country, Subdivision = subdivision!, Postcode = postcode!, Name = name, Category = category };

	// A country with subdivisions NSW, ACT and QLD (QLD retired) and localities Sydney 2000 (NSW)
	// and Hume 2620 (ACT).
	private async Task<string> SeedAsync()
	{
		string code = NewCountry();

		await _admin.CreateCountryAsync( AustraliaLike( code ) );

		foreach( string subdivision in new[] { "NSW", "ACT", "QLD" } )
			await _admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = code, Code = subdivision, Name = "State " + subdivision } );

		await _admin.RetireSubdivisionAsync( code, "QLD" );
		await _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2000", "Sydney" ) );
		await _admin.CreateLocalityAsync( LocalityOf( code, "ACT", "2620", "Hume" ) );

		return code;
	}

	private async Task<DateTime> VersionAsync( string code ) => ( await _admin.GetCountryAsync( code ) ).CurrentVersion;

	private Task<ImportRun> ImportAsync( string code, params string[] records ) =>
		ImportAsync( _admin, code, records );

	private static Task<ImportRun> ImportAsync( LocalityAdministration admin, string code, params string[] records ) =>
		admin.ImportAsync( code,
			new ImportRequest { Importer = "CSV", Source = "test.csv", Options = new Dictionary<string, string> { [ "defaultCategory" ] = "TOWN" } },
			new MemoryStream( Encoding.UTF8.GetBytes( "subdivision,postcode,locality\n" + string.Join( "\n", records ) ) ) );

	private async Task<string[]> LocalitiesAsync( string code, bool includeRetired = false ) =>
		( await _admin.GetLocalitiesAsync( code, includeRetired: includeRetired ) ).Value
		.Select( l => $"{l.Definition.Subdivision}|{l.Definition.Postcode}|{l.Definition.Name}{( l.Retired is null ? "" : " (retired)" )}" )
		.ToArray();

	private LocalityChange[] ChangesOf( string code ) => _eventPublisher.Changes.Where( c => c.Country == code ).ToArray();

	// ActionContext compares by reference, so records holding one are compared through these.
	private static object Snapshot( Country country ) =>
		( country.Definition, country.CurrentVersion, country.Retired, country.Registration.Timestamp, country.LastUpdate?.Timestamp );

	private static object Snapshot( Versioned<Locality> locality ) =>
		( locality.Value.Definition, locality.Value.Retired, locality.Value.Registration.Timestamp, locality.Value.LastUpdate?.Timestamp,
			locality.DatasetVersion );

	// == Authorization ==

	public static TheoryData<string, Func<ILocalityAdministration, Task>> Operations() => new()
	{
		{ SecurityOperations.View, a => a.GetCountriesAsync() },
		{ SecurityOperations.View, a => a.GetCountryVersionsAsync() },
		{ SecurityOperations.View, a => a.GetCountryAsync( "ZZ" ) },
		{ SecurityOperations.View, a => a.GetSubdivisionsAsync( "ZZ" ) },
		{ SecurityOperations.View, a => a.GetSubdivisionAsync( "ZZ", "X" ) },
		{ SecurityOperations.View, a => a.GetLocalitiesAsync( "ZZ" ) },
		{ SecurityOperations.View, a => a.GetLocalityAsync( "ZZ", "X", "1", "A" ) },
		{ SecurityOperations.View, a => a.SearchLocalitiesAsync( "ZZ", "A" ) },
		{ SecurityOperations.Create, a => a.CreateCountryAsync( AustraliaLike( "ZZ" ) with { Code = "Z" } ) },
		{ SecurityOperations.Modify, a => a.UpdateCountryAsync( AustraliaLike( "ZZ" ) ) },
		{ SecurityOperations.Retire, a => a.RetireCountryAsync( "ZZ" ) },
		{ SecurityOperations.Retire, a => a.ReinstateCountryAsync( "ZZ" ) },
		{ SecurityOperations.Create, a => a.CreateSubdivisionAsync( new SubdivisionDefinition { Country = "ZZ", Code = "X", Name = "X" } ) },
		{ SecurityOperations.Modify, a => a.UpdateSubdivisionAsync( new SubdivisionDefinition { Country = "ZZ", Code = "X", Name = "X" } ) },
		{ SecurityOperations.Retire, a => a.RetireSubdivisionAsync( "ZZ", "X" ) },
		{ SecurityOperations.Retire, a => a.ReinstateSubdivisionAsync( "ZZ", "X" ) },
		{ SecurityOperations.Create, a => a.CreateLocalityAsync( LocalityOf( "ZZ", "X", "1", "A" ) ) },
		{ SecurityOperations.Retire, a => a.RetireLocalityAsync( "ZZ", "X", "1", "A" ) },
		{ SecurityOperations.Retire, a => a.ReinstateLocalityAsync( "ZZ", "X", "1", "A" ) },
		{ SecurityOperations.Delete, a => a.DeleteLocalityAsync( "ZZ", "X", "1", "A" ) },
		{ SecurityOperations.Import, a => a.ImportAsync( "ZZ", new ImportRequest { Importer = "csv" }, new MemoryStream() ) },
		{ SecurityOperations.Import, a => a.GetImportRunsAsync( "ZZ" ) },
		{ SecurityOperations.View, a => a.GetCategoriesAsync() },
		{ SecurityOperations.View, a => a.GetCategoryAsync( "ZZZ" ) },
		{ SecurityOperations.View, a => a.GetCountryCategoriesAsync( "ZZ" ) },
		{ SecurityOperations.View, a => a.GetCategoryCountriesAsync( "ZZZ" ) },
		{ SecurityOperations.Create, a => a.CreateCategoryAsync( new CategoryDefinition { Code = " ", Name = "X" } ) },
		{ SecurityOperations.Modify, a => a.UpdateCategoryAsync( new CategoryDefinition { Code = "ZZZ", Name = "X" } ) },
		{ SecurityOperations.Retire, a => a.RetireCategoryAsync( "ZZZ" ) },
		{ SecurityOperations.Retire, a => a.ReinstateCategoryAsync( "ZZZ" ) },
		{ SecurityOperations.Modify, a => a.AssignCategoryAsync( "ZZ", "X" ) },
		{ SecurityOperations.Modify, a => a.UnassignCategoryAsync( "ZZ", "X" ) },
		{ SecurityOperations.Retire, a => a.DisableCategoryAsync( "ZZ", "X" ) },
		{ SecurityOperations.Retire, a => a.EnableCategoryAsync( "ZZ", "X" ) }
	};

	// Granted every other operation, the call is refused; granted its own, it gets past
	// authorization (and then fails on the unknown country or the invalid definition).
	[Theory]
	[MemberData( nameof(Operations) )]
	public async Task EachOperationRequiresItsOwnPermission( string operation, Func<ILocalityAdministration, Task> call )
	{
		string[] all = { SecurityOperations.View, SecurityOperations.Create, SecurityOperations.Modify, SecurityOperations.Retire,
						SecurityOperations.Delete, SecurityOperations.Import };

		await Assert.ThrowsAsync<AccessDeniedException>( () => call( Admin( SecurityManagers.Granting( all.Where( o => o != operation ).ToArray() ) ) ) );
		await Assert.ThrowsAsync<AccessDeniedException>( () => call( Admin( SecurityManagers.WithoutPrincipal() ) ) );
		await Assert.ThrowsAsync<AccessDeniedException>( () => call( Admin( SecurityManagers.Unauthenticated() ) ) );

		Exception? passed = await Record.ExceptionAsync( () => call( Admin( SecurityManagers.Granting( operation ) ) ) );

		Assert.False( passed is AccessDeniedException );
	}

	// == Countries ==

	[Fact]
	public async Task CreateCountryTidiesTheDefinitionAndStampsItsVersion()
	{
		string code = NewCountry();

		Country country = await _admin.CreateCountryAsync( AustraliaLike( code.ToLowerInvariant() ) with
		{
			Alpha3 = " aus ", Name = "  Australia ", PostcodePattern = " "
		} );

		Assert.Equal( ( code, "AUS", "Australia", (string?)null ),
					( country.Definition.Code, country.Definition.Alpha3, country.Definition.Name, country.Definition.PostcodePattern ) );
		Assert.Equal( country.CurrentVersion, country.Registration.Timestamp );
		Assert.Equal( "alice", country.Registration.Actor.Identifier );
		Assert.Null( country.LastUpdate );
		Assert.Equal( Snapshot( country ), Snapshot( await _admin.GetCountryAsync( code ) ) );

		Assert.Equal( new[]
					{
						( ChangeEntityTypes.Country, code, ChangeKinds.Created, country.CurrentVersion ),
						( ChangeEntityTypes.CountryCategory, "TOWN", ChangeKinds.Created, country.CurrentVersion ),
						( ChangeEntityTypes.CountryCategory, "SUBURB", ChangeKinds.Created, country.CurrentVersion )
					},
					ChangesOf( code ).Select( c => ( c.EntityType, c.Key, c.Kind, c.DatasetVersion ) ) );
		Assert.Null( ( await _admin.GetCountryAsync( code ) ).Definition.Categories );
	}

	[Fact]
	public async Task CreateCountryRejectsInvalidAndDuplicateDefinitions()
	{
		string code = NewCountry();

		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateCountryAsync( AustraliaLike( "AUS" ) ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateCountryAsync( AustraliaLike( code ) with { Alpha3 = "AU" } ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateCountryAsync( AustraliaLike( code ) with { Name = " " } ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateCountryAsync( AustraliaLike( code ) with { PostcodePattern = "[" } ) );

		await _admin.CreateCountryAsync( AustraliaLike( code ) );
		DuplicateEntryException duplicate = await Assert.ThrowsAsync<DuplicateEntryException>( () => _admin.CreateCountryAsync( AustraliaLike( code ) ) );

		Assert.Equal( ( ChangeEntityTypes.Country, code ), ( duplicate.EntityType, duplicate.Key ) );
	}

	[Fact]
	public async Task UpdateCountryBumpsTheVersionAndStampsLastUpdate()
	{
		string code = await SeedAsync();
		DateTime before = await VersionAsync( code );

		Country updated = await _admin.UpdateCountryAsync( AustraliaLike( code ) with { Name = "Renamed" } );

		Assert.Equal( "Renamed", updated.Definition.Name );
		Assert.True( updated.CurrentVersion > before );
		Assert.Equal( updated.CurrentVersion, updated.LastUpdate.Timestamp );
		Assert.Equal( ChangeKinds.Updated, ChangesOf( code ).Last().Kind );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _admin.UpdateCountryAsync( AustraliaLike( NewCountry() ) ) );
	}

	[Fact]
	public async Task HasSubdivisionsCannotChangeWhileTheCountryHoldsLocalities()
	{
		string code = await SeedAsync();
		DateTime before = await VersionAsync( code );

		await Assert.ThrowsAsync<ArgumentException>( () => _admin.UpdateCountryAsync( AustraliaLike( code ) with { HasSubdivisions = false } ) );

		Assert.Equal( before, await VersionAsync( code ) );

		string empty = NewCountry();
		await _admin.CreateCountryAsync( AustraliaLike( empty ) );
		Assert.False( ( await _admin.UpdateCountryAsync( AustraliaLike( empty ) with { HasSubdivisions = false } ) ).Definition.HasSubdivisions );
	}

	[Fact]
	public async Task RetireAndReinstateCountry()
	{
		string code = await SeedAsync();

		Country retired = await _admin.RetireCountryAsync( code );
		int published = ChangesOf( code ).Length;
		Country again = await _admin.RetireCountryAsync( code.ToLowerInvariant() );
		Country reinstated = await _admin.ReinstateCountryAsync( code );

		Assert.Equal( retired.CurrentVersion, retired.Retired );
		Assert.Equal( Snapshot( retired ), Snapshot( again ) );
		Assert.Equal( published + 1, ChangesOf( code ).Length );
		Assert.Null( reinstated.Retired );
		Assert.True( reinstated.CurrentVersion > retired.CurrentVersion );
		Assert.Equal( new[] { ChangeKinds.Retired, ChangeKinds.Reinstated }, ChangesOf( code ).TakeLast( 2 ).Select( c => c.Kind ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _admin.RetireCountryAsync( NewCountry() ) );
	}

	// == Subdivisions ==

	[Fact]
	public async Task SubdivisionWritesAreVersionedAndPublished()
	{
		string code = await SeedAsync();
		DateTime before = await VersionAsync( code );

		Versioned<Subdivision> created = await _admin.CreateSubdivisionAsync(
			new SubdivisionDefinition { Country = code.ToLowerInvariant(), Code = " VIC ", Name = " Victoria ", Type = " " } );
		Versioned<Subdivision> updated = await _admin.UpdateSubdivisionAsync(
			new SubdivisionDefinition { Country = code, Code = "VIC", Name = "Victoria", Type = SubdivisionTypes.State } );

		Assert.True( created.DatasetVersion > before );
		Assert.Equal( ( code, "VIC", "Victoria", (string?)null ),
					( created.Value.Definition.Country, created.Value.Definition.Code, created.Value.Definition.Name, created.Value.Definition.Type ) );
		Assert.Equal( created.DatasetVersion, created.Value.Registration.Timestamp );
		Assert.Equal( SubdivisionTypes.State, updated.Value.Definition.Type );
		Assert.Equal( updated.DatasetVersion, updated.Value.LastUpdate.Timestamp );
		Assert.Equal( updated.DatasetVersion, await VersionAsync( code ) );
		Assert.Equal( new[] { ChangeKinds.Created, ChangeKinds.Updated }, ChangesOf( code ).TakeLast( 2 ).Select( c => c.Kind ) );
		Assert.All( ChangesOf( code ).TakeLast( 2 ), c => Assert.Equal( ( ChangeEntityTypes.Subdivision, "VIC" ), ( c.EntityType, c.Key ) ) );
	}

	[Fact]
	public async Task SubdivisionWriteFailures()
	{
		string code = await SeedAsync();
		string flat = NewCountry();
		await _admin.CreateCountryAsync( AustraliaLike( flat ) with { HasSubdivisions = false } );
		DateTime before = await VersionAsync( code );

		await Assert.ThrowsAsync<DuplicateEntryException>(
			() => _admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = code, Code = "NSW", Name = "Again" } ) );
		await Assert.ThrowsAsync<ArgumentException>(
			() => _admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = flat, Code = "X", Name = "X" } ) );
		await Assert.ThrowsAsync<UnknownSubdivisionException>(
			() => _admin.UpdateSubdivisionAsync( new SubdivisionDefinition { Country = code, Code = "TAS", Name = "Tasmania" } ) );
		await Assert.ThrowsAsync<UnknownSubdivisionException>( () => _admin.RetireSubdivisionAsync( code, "TAS" ) );
		await Assert.ThrowsAsync<UnknownCountryException>(
			() => _admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = NewCountry(), Code = "X", Name = "X" } ) );

		Assert.Equal( before, await VersionAsync( code ) );
	}

	[Fact]
	public async Task RetireAndReinstateSubdivision()
	{
		string code = await SeedAsync();

		Versioned<Subdivision> unchanged = await _admin.RetireSubdivisionAsync( code, "QLD" );
		Versioned<Subdivision> reinstated = await _admin.ReinstateSubdivisionAsync( code, "QLD" );

		Assert.NotNull( unchanged.Value.Retired );
		Assert.Null( reinstated.Value.Retired );
		Assert.True( reinstated.DatasetVersion > unchanged.DatasetVersion );
		Assert.Equal( 2, ChangesOf( code ).Count( c => c.EntityType == ChangeEntityTypes.Subdivision && c.Key == "QLD" && c.Kind != ChangeKinds.Created ) );
	}

	// == Localities ==

	[Fact]
	public async Task CreateLocalityChecksItAgainstTheCountry()
	{
		string code = await SeedAsync();
		string flat = NewCountry();
		await _admin.CreateCountryAsync( new CountryDefinition { Code = flat, Name = "Flat", HasSubdivisions = false, Categories = new[] { "TOWN" } } );
		DateTime before = await VersionAsync( code );

		await Assert.ThrowsAsync<UnknownSubdivisionException>( () => _admin.CreateLocalityAsync( LocalityOf( code, "TAS", "7000", "Hobart" ) ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateLocalityAsync( LocalityOf( code, null, "2000", "Nowhere" ) ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateLocalityAsync( LocalityOf( code, "NSW", null, "Nowhere" ) ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "20000", "Nowhere" ) ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateLocalityAsync( LocalityOf( flat, "NSW", null, "Nowhere" ) ) );

		DuplicateEntryException duplicate =
			await Assert.ThrowsAsync<DuplicateEntryException>( () => _admin.CreateLocalityAsync( LocalityOf( code, "NSW", " 2000", "  SYDNEY " ) ) );

		Assert.Equal( ( ChangeEntityTypes.Locality, "NSW/2000/SYDNEY" ), ( duplicate.EntityType, duplicate.Key ) );
		Assert.Equal( before, await VersionAsync( code ) );

		Versioned<Locality> yaren = await _admin.CreateLocalityAsync( LocalityOf( flat, " ", " ", " Yaren " ) );

		Assert.Equal( ( (string?)null, (string?)null, "Yaren" ),
					( yaren.Value.Definition.Subdivision, yaren.Value.Definition.Postcode, yaren.Value.Definition.Name ) );
	}

	[Fact]
	public async Task LocalityUnderARetiredSubdivisionCanBeCreated()
	{
		string code = await SeedAsync();

		Versioned<Locality> created = await _admin.CreateLocalityAsync( LocalityOf( code, "QLD", "4000", "Brisbane" ) );

		Assert.Equal( created.DatasetVersion, created.Value.Registration.Timestamp );
		Assert.Equal( created.DatasetVersion, await VersionAsync( code ) );

		LocalityChange change = ChangesOf( code ).Last();
		Assert.Equal( ( ChangeEntityTypes.Locality, "QLD", "4000", "Brisbane", ChangeKinds.Created ),
					( change.EntityType, change.Subdivision, change.Postcode, change.Key, change.Kind ) );
	}

	[Fact]
	public async Task RetireReinstateAndDeleteLocality()
	{
		string code = await SeedAsync();

		Versioned<Locality> retired = await _admin.RetireLocalityAsync( code, "NSW", "2000", "sydney" );
		Versioned<Locality> again = await _admin.RetireLocalityAsync( code, "NSW", "2000", "Sydney" );
		Versioned<Locality> reinstated = await _admin.ReinstateLocalityAsync( code, "NSW", "2000", "Sydney" );
		DateTime deleted = await _admin.DeleteLocalityAsync( code, "NSW", "2000", "SYDNEY" );

		Assert.Equal( retired.DatasetVersion, retired.Value.Retired );
		Assert.Equal( retired.DatasetVersion, retired.Value.LastUpdate.Timestamp );
		Assert.Equal( Snapshot( retired ), Snapshot( again ) );
		Assert.Null( reinstated.Value.Retired );
		Assert.True( deleted > reinstated.DatasetVersion );
		Assert.Equal( deleted, await VersionAsync( code ) );
		Assert.Equal( new[] { ChangeKinds.Retired, ChangeKinds.Reinstated, ChangeKinds.Deleted },
					ChangesOf( code ).TakeLast( 3 ).Select( c => c.Kind ) );
		Assert.Equal( "Sydney", ChangesOf( code ).Last().Key );
		await Assert.ThrowsAsync<UnknownLocalityException>( () => _admin.GetLocalityAsync( code, "NSW", "2000", "Sydney" ) );
		await Assert.ThrowsAsync<UnknownLocalityException>( () => _admin.DeleteLocalityAsync( code, "NSW", "2000", "Sydney" ) );
		Assert.Equal( deleted, await VersionAsync( code ) );
	}

	[Fact]
	public async Task APublisherFailureNeverFailsTheWrite()
	{
		string code = NewCountry();
		RecordingEventPublisher failing = new( fail: true );
		LocalityAdministration admin = Admin( publisher: failing );

		await admin.CreateCountryAsync( AustraliaLike( code ) with { HasSubdivisions = false, PostcodeRequired = false } );
		await admin.CreateLocalityAsync( LocalityOf( code, null, null, "Yaren" ) );
		ImportRun run = await ImportAsync( admin, code, ",,Yaren" );

		// The country, its two categories (D28) and the locality.
		Assert.Equal( 4, failing.Changes.Count );
		Assert.Single( failing.Imports );
		Assert.Equal( run.DatasetVersion, await VersionAsync( code ) );
	}

	[Fact]
	public async Task AdministrationReadsReturnAuditContexts()
	{
		string code = await SeedAsync();

		Versioned<Locality> sydney = await _admin.GetLocalityAsync( code, "NSW", "2000", "Sydney" );
		Versioned<IEnumerable<Locality>> search = await _admin.SearchLocalitiesAsync( code, "h" );

		Assert.Equal( "alice", sydney.Value.Registration.Actor.Identifier );
		Assert.Equal( "Hume", Assert.Single( search.Value ).Definition.Name );
		Assert.Contains( await _admin.GetCountryVersionsAsync(), v => v.Country == code );
	}

	// == Imports ==

	[Fact]
	public async Task ImportMakesTheCountryMatchTheDataset()
	{
		string code = await SeedAsync();
		await _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2870", "Parkes" ) );
		await _admin.RetireLocalityAsync( code, "NSW", "2870", "Parkes" );
		DateTime before = await VersionAsync( code );
		int changesBefore = ChangesOf( code ).Length;

		ImportRun run = await ImportAsync( code,
			"NSW,2000,  sydney ",      // existing, current: unchanged, keeps its display name
			"NSW,2870,Parkes",         // existing, retired: reinstated
			"ACT,2600,Parkes",         // new
			"ACT,2600,PARKES",         // the same locality again
			"QLD,4000,Brisbane" );     // under a retired subdivision: accepted

		Assert.Equal( ( 2, 1, 1, 1 ), ( run.Added, run.Reinstated, run.Unchanged, run.Deleted ) );
		Assert.Equal( ( code, "CSV", "test.csv", "alice" ), ( run.Country, run.Importer, run.Source, run.Performed.Actor.Identifier ) );
		Assert.True( run.DatasetVersion > before );
		Assert.Equal( run.DatasetVersion, run.Performed.Timestamp );
		Assert.Equal( run.DatasetVersion, await VersionAsync( code ) );

		Assert.Equal( new[] { "ACT|2600|Parkes", "NSW|2870|Parkes", "NSW|2000|Sydney", "QLD|4000|Brisbane" }, await LocalitiesAsync( code, true ) );

		Locality sydney = ( await _admin.GetLocalityAsync( code, "NSW", "2000", "Sydney" ) ).Value;
		Locality parkes = ( await _admin.GetLocalityAsync( code, "ACT", "2600", "Parkes" ) ).Value;

		Assert.Equal( run.DatasetVersion, sydney.LastUpdate.Timestamp );
		Assert.True( sydney.Registration.Timestamp < run.DatasetVersion );
		Assert.Equal( run.DatasetVersion, parkes.Registration.Timestamp );
		Assert.Null( parkes.LastUpdate );
		Assert.NotNull( ( await _admin.GetSubdivisionAsync( code, "QLD" ) ).Value.Retired );

		Assert.Equal( changesBefore, ChangesOf( code ).Length );
		Assert.Same( run, Assert.Single( _eventPublisher.Imports, i => i.Country == code ) );
		Assert.Equal( run.Identifier, Assert.Single( await _admin.GetImportRunsAsync( code ) ).Identifier );
	}

	[Fact]
	public async Task EveryImportBumpsTheVersionEvenWhenNothingChanges()
	{
		string code = await SeedAsync();

		ImportRun first = await ImportAsync( code, "NSW,2000,Sydney", "ACT,2620,Hume" );
		ImportRun second = await ImportAsync( code, "NSW,2000,Sydney", "ACT,2620,Hume" );

		Assert.Equal( ( 0, 0, 2, 0 ), ( second.Added, second.Reinstated, second.Unchanged, second.Deleted ) );
		Assert.True( second.DatasetVersion > first.DatasetVersion );
		Assert.Equal( new[] { second.Identifier, first.Identifier }, ( await _admin.GetImportRunsAsync( code ) ).Select( r => r.Identifier ) );
	}

	public static TheoryData<string> InvalidRows() => new()
	{
		"TAS,7000,Hobart",   // unknown subdivision
		",2000,Nowhere",     // missing subdivision
		"NSW,,Nowhere",      // missing required postcode
		"NSW,200,Nowhere",   // postcode not matching the pattern
		"NSW,2000,"          // missing name
	};

	[Theory]
	[MemberData( nameof(InvalidRows) )]
	public async Task AnInvalidRowFailsTheWholeImport( string invalid )
	{
		string code = await SeedAsync();
		DateTime before = await VersionAsync( code );
		string[] localities = await LocalitiesAsync( code, true );

		DatasetImportException failure = await Assert.ThrowsAsync<DatasetImportException>(
			() => ImportAsync( code, "NSW,2001,Sydney", invalid ) );

		Assert.Equal( 2, failure.Position );
		Assert.Equal( before, await VersionAsync( code ) );
		Assert.Equal( localities, await LocalitiesAsync( code, true ) );
		Assert.DoesNotContain( _eventPublisher.Imports, i => i.Country == code );
		Assert.Empty( await _admin.GetImportRunsAsync( code ) );
	}

	[Fact]
	public async Task ImportRefusals()
	{
		string code = await SeedAsync();
		string flat = NewCountry();
		await _admin.CreateCountryAsync( new CountryDefinition { Code = flat, Name = "Flat", HasSubdivisions = false, Categories = new[] { "TOWN" } } );

		await Assert.ThrowsAsync<DatasetImportException>( () => ImportAsync( code ) );
		await Assert.ThrowsAsync<DatasetImportException>( () => ImportAsync( flat, "X,,Yaren" ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => ImportAsync( NewCountry(), "NSW,2000,Sydney" ) );
		await Assert.ThrowsAsync<UnknownDatasetImporterException>(
			() => _admin.ImportAsync( code, new ImportRequest { Importer = "xml" }, new MemoryStream() ) );
		Assert.Equal( 2, ( await LocalitiesAsync( code ) ).Length );
	}

	// == Categories (DECISIONS D28) ==

	// A global category code of the test's own.
	private static string NewCategory() => "K" + NewCountry();

	private string[] CategoriesOf( IEnumerable<Locality> localities ) =>
		localities.Select( l => $"{l.Definition.Name}={l.Definition.Category}" ).ToArray();

	[Fact]
	public async Task GlobalCategoriesAreCreatedRenamedRetiredAndReinstated()
	{
		string code = NewCategory();

		Category created = await _admin.CreateCategoryAsync( new CategoryDefinition { Code = $" {code} ", Name = "  Village " } );
		Category renamed = await _admin.UpdateCategoryAsync( new CategoryDefinition { Code = code, Name = "Hamlet" } );
		Category retired = await _admin.RetireCategoryAsync( code );
		Category again = await _admin.RetireCategoryAsync( code );
		Category reinstated = await _admin.ReinstateCategoryAsync( code );

		Assert.Equal( ( code, "Village" ), ( created.Definition.Code, created.Definition.Name ) );
		Assert.Equal( "Hamlet", renamed.Definition.Name );
		Assert.NotNull( renamed.LastUpdate );
		Assert.NotNull( retired.Retired );
		Assert.Equal( retired.Retired, again.Retired );
		Assert.Null( reinstated.Retired );
		Assert.Equal( "Hamlet", ( await _admin.GetCategoryAsync( code ) ).Definition.Name );
		Assert.Contains( await _admin.GetCategoriesAsync(), c => c.Definition.Code == code );

		// Changes to the global list publish nothing (D28).
		Assert.Empty( _eventPublisher.Changes );

		DuplicateEntryException duplicate = await Assert.ThrowsAsync<DuplicateEntryException>(
			() => _admin.CreateCategoryAsync( new CategoryDefinition { Code = code, Name = "Again" } ) );
		UnknownCategoryException unknown = await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.GetCategoryAsync( code + "X" ) );

		Assert.Equal( ( ChangeEntityTypes.Category, code ), ( duplicate.EntityType, duplicate.Key ) );
		Assert.Equal( ( (string?)null, code + "X" ), ( unknown.Country, unknown.Category ) );
		await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.UpdateCategoryAsync( new CategoryDefinition { Code = code + "X", Name = "X" } ) );
		await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.RetireCategoryAsync( code + "X" ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateCategoryAsync( new CategoryDefinition { Code = code + "Y", Name = " " } ) );
	}

	[Fact]
	public async Task CategoryOperationsAreCheckedAgainstTheirResource()
	{
		string code = await SeedAsync();
		LocalityAdministration countriesOnly = Admin( SecurityManagers.GrantingOn( SecurityObjectTypes.Country ) );
		LocalityAdministration categoriesOnly = Admin( SecurityManagers.GrantingOn( SecurityObjectTypes.Category ) );

		await Assert.ThrowsAsync<AccessDeniedException>(
			() => countriesOnly.CreateCategoryAsync( new CategoryDefinition { Code = NewCategory(), Name = "X" } ) );
		await Assert.ThrowsAsync<AccessDeniedException>( () => countriesOnly.GetCategoriesAsync() );
		await Assert.ThrowsAsync<AccessDeniedException>( () => categoriesOnly.AssignCategoryAsync( code, "SUBURB" ) );
		await Assert.ThrowsAsync<AccessDeniedException>( () => categoriesOnly.GetCountryCategoriesAsync( code ) );

		await categoriesOnly.CreateCategoryAsync( new CategoryDefinition { Code = NewCategory(), Name = "X" } );
		await countriesOnly.DisableCategoryAsync( code, "SUBURB" );
	}

	[Fact]
	public async Task ACountryIsCreatedWithCurrentCategoriesOnly()
	{
		string code = NewCountry();
		string retired = NewCategory();

		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = retired, Name = "Retired" } );
		await _admin.RetireCategoryAsync( retired );

		ArgumentException none = await Assert.ThrowsAsync<ArgumentException>(
			() => _admin.CreateCountryAsync( AustraliaLike( code ) with { Categories = new[] { " " } } ) );
		UnknownCategoryException unknown = await Assert.ThrowsAsync<UnknownCategoryException>(
			() => _admin.CreateCountryAsync( AustraliaLike( code ) with { Categories = new[] { "TOWN", "NOPE" } } ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateCountryAsync( AustraliaLike( code ) with { Categories = new[] { retired } } ) );

		Assert.Equal( nameof(CountryDefinition.Categories), none.ParamName );
		Assert.Equal( ( (string?)null, "NOPE" ), ( unknown.Country, unknown.Category ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _admin.GetCountryAsync( code ) );

		await _admin.CreateCountryAsync( AustraliaLike( code ) with { Categories = new[] { " TOWN", "TOWN " } } );

		Versioned<IEnumerable<CountryCategory>> categories = await _admin.GetCountryCategoriesAsync( code );
		CountryCategory town = Assert.Single( categories.Value );
		Assert.Equal( ( code, "TOWN", "Town", (DateTime?)null, "alice" ), ( town.Country, town.Category, town.Name, town.Disabled, town.Registration.Actor.Identifier ) );
		Assert.Equal( await VersionAsync( code ), categories.DatasetVersion );
	}

	[Fact]
	public async Task AssigningAndUnassigning()
	{
		string code = await SeedAsync();
		string extra = NewCategory();
		string retired = NewCategory();

		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = extra, Name = "Extra" } );
		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = retired, Name = "Retired" } );
		await _admin.RetireCategoryAsync( retired );

		Versioned<CountryCategory> assigned = await _admin.AssignCategoryAsync( code, $" {extra} " );
		Versioned<CountryCategory> again = await _admin.AssignCategoryAsync( code, extra );

		Assert.Equal( ( extra, "Extra", (DateTime?)null ), ( assigned.Value.Category, assigned.Value.Name, assigned.Value.Disabled ) );
		Assert.Equal( assigned.DatasetVersion, again.DatasetVersion );
		Assert.Equal( assigned.DatasetVersion, await VersionAsync( code ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.AssignCategoryAsync( code, retired ) );
		Assert.Null( ( await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.AssignCategoryAsync( code, "NOPE" ) ) ).Country );

		// Sydney and Hume are TOWNs: TOWN is held, SUBURB and extra are not.
		ArgumentException held = await Assert.ThrowsAsync<ArgumentException>( () => _admin.UnassignCategoryAsync( code, "TOWN" ) );
		DateTime unassigned = await _admin.UnassignCategoryAsync( code, extra );

		Assert.Contains( "held by localities", held.Message );
		Assert.Equal( unassigned, await VersionAsync( code ) );
		Assert.Equal( new[] { "SUBURB", "TOWN" }, ( await _admin.GetCountryCategoriesAsync( code ) ).Value.Select( c => c.Category ) );
		Assert.Equal( code, ( await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.UnassignCategoryAsync( code, extra ) ) ).Country );

		// SUBURB is unused but, once TOWN is disabled, the only enabled category.
		await _admin.DisableCategoryAsync( code, "TOWN" );
		Assert.Equal( code, ( await Assert.ThrowsAsync<CountryConfigurationException>( () => _admin.UnassignCategoryAsync( code, "SUBURB" ) ) ).Country );

		Assert.Equal( new[] { ( ChangeKinds.Created, extra ), ( ChangeKinds.Deleted, extra ), ( ChangeKinds.Retired, "TOWN" ) },
					ChangesOf( code ).Where( c => c.EntityType == ChangeEntityTypes.CountryCategory && c.Performed.Timestamp > assigned.DatasetVersion.AddTicks( -1 ) )
									.Select( c => ( c.Kind, c.Key ) ) );
	}

	[Fact]
	public async Task DisablingRefusesNewLocalitiesAndKeepsAnEnabledCategory()
	{
		string code = await SeedAsync();
		await _admin.RetireLocalityAsync( code, "ACT", "2620", "Hume" );

		Versioned<CountryCategory> disabled = await _admin.DisableCategoryAsync( code, "TOWN" );
		Versioned<CountryCategory> again = await _admin.DisableCategoryAsync( code, "town".ToUpperInvariant() );

		Assert.Equal( disabled.DatasetVersion, disabled.Value.Disabled );
		Assert.Equal( disabled.DatasetVersion, again.DatasetVersion );
		Assert.Equal( new[] { "Hume=TOWN", "Sydney=TOWN" }, CategoriesOf( ( await _admin.GetLocalitiesAsync( code, includeRetired: true ) ).Value ) );

		ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>( () => _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2001", "Haymarket" ) ) );
		CountryConfigurationException last = await Assert.ThrowsAsync<CountryConfigurationException>( () => _admin.DisableCategoryAsync( code, "SUBURB" ) );
		await Assert.ThrowsAsync<DatasetImportException>( () => ImportAsync( code, "NSW,2000,Sydney" ) );

		Assert.Contains( "disabled", refused.Message );
		Assert.Contains( "at least one enabled category", last.Message );
		Assert.Equal( code, last.Country );
		Assert.Null( ( await _admin.GetCountryCategoriesAsync( code ) ).Value.Single( c => c.Category == "SUBURB" ).Disabled );

		Versioned<CountryCategory> enabled = await _admin.EnableCategoryAsync( code, "TOWN" );
		Versioned<CountryCategory> stillEnabled = await _admin.EnableCategoryAsync( code, "TOWN" );

		Assert.Null( enabled.Value.Disabled );
		Assert.Equal( enabled.DatasetVersion, stillEnabled.DatasetVersion );
		await _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2001", "Haymarket" ) );
	}

	[Fact]
	public async Task DisablingWithAReplacementMovesTheLocalities()
	{
		string code = await SeedAsync();
		await _admin.RetireLocalityAsync( code, "ACT", "2620", "Hume" );
		await _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2010", "Surry Hills", "SUBURB" ) );

		await Assert.ThrowsAsync<ArgumentException>( () => _admin.DisableCategoryAsync( code, "TOWN", "TOWN" ) );
		Assert.Equal( code, ( await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.DisableCategoryAsync( code, "TOWN", "NOPE" ) ) ).Country );

		Versioned<CountryCategory> disabled = await _admin.DisableCategoryAsync( code, "TOWN", " SUBURB " );

		Locality[] all = ( await _admin.GetLocalitiesAsync( code, includeRetired: true ) ).Value.ToArray();
		Locality hume = all.Single( l => l.Definition.Name == "Hume" );

		Assert.Equal( new[] { "Hume=SUBURB", "Surry Hills=SUBURB", "Sydney=SUBURB" }, CategoriesOf( all ) );
		Assert.NotNull( hume.Retired );
		Assert.Equal( disabled.DatasetVersion, hume.LastUpdate!.Timestamp );
		Assert.Equal( disabled.DatasetVersion, disabled.Value.Disabled );
		Assert.Empty( ( await _admin.GetLocalitiesAsync( code, category: "TOWN", includeRetired: true ) ).Value );

		// The disable is a country category change; the moved localities are one replacement event.
		LocalityChange change = ChangesOf( code ).Last();
		CategoryReplacement replacement = Assert.Single( _eventPublisher.Replacements );

		Assert.Equal( ( ChangeEntityTypes.CountryCategory, "TOWN", ChangeKinds.Retired, disabled.DatasetVersion ),
					( change.EntityType, change.Key, change.Kind, change.DatasetVersion ) );
		Assert.Equal( ( code, "TOWN", "SUBURB", 2, disabled.DatasetVersion, disabled.DatasetVersion, "alice" ),
					( replacement.Country, replacement.Category, replacement.Replacement, replacement.Localities, replacement.DatasetVersion,
						replacement.Performed.Timestamp, replacement.Performed.Actor.Identifier ) );

		// The replacement must be enabled; a disabled category can be given again to move what
		// still holds it, and with nothing left to move it changes nothing.
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.DisableCategoryAsync( code, "SUBURB", "TOWN" ) );
		await _admin.EnableCategoryAsync( code, "TOWN" );
		await _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2011", "Potts Point", "TOWN" ) );
		await _admin.DisableCategoryAsync( code, "TOWN" );
		int changes = ChangesOf( code ).Length;
		Versioned<CountryCategory> moved = await _admin.DisableCategoryAsync( code, "TOWN", "SUBURB" );
		Versioned<CountryCategory> nothingToMove = await _admin.DisableCategoryAsync( code, "TOWN", "SUBURB" );

		Assert.True( moved.DatasetVersion > moved.Value.Disabled );
		Assert.Equal( "SUBURB", ( await _admin.GetLocalityAsync( code, "NSW", "2011", "Potts Point" ) ).Value.Definition.Category );
		Assert.Equal( changes, ChangesOf( code ).Length );
		Assert.Equal( new[] { 2, 1 }, _eventPublisher.Replacements.Select( r => r.Localities ) );
		Assert.Equal( moved.DatasetVersion, nothingToMove.DatasetVersion );
		Assert.Equal( moved.DatasetVersion, await VersionAsync( code ) );
	}

	[Fact]
	public async Task AReplacementThatMovesNothingPublishesNoReplacement()
	{
		string code = await SeedAsync();
		string unused = NewCategory();

		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = unused, Name = "Unused" } );
		await _admin.AssignCategoryAsync( code, unused );

		Versioned<CountryCategory> disabled = await _admin.DisableCategoryAsync( code, unused, "TOWN" );

		Assert.Equal( disabled.DatasetVersion, disabled.Value.Disabled );
		Assert.Equal( ( unused, ChangeKinds.Retired ), ( ChangesOf( code ).Last().Key, ChangesOf( code ).Last().Kind ) );
		Assert.Empty( _eventPublisher.Replacements );
	}

	[Fact]
	public async Task EnablingRefusesARetiredGlobalCategory()
	{
		string code = await SeedAsync();
		string retiring = NewCategory();

		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = retiring, Name = "Retiring" } );
		await _admin.AssignCategoryAsync( code, retiring );
		await _admin.DisableCategoryAsync( code, retiring );
		await _admin.RetireCategoryAsync( retiring );

		UnknownCategoryException unknown = await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.EnableCategoryAsync( code, retiring + "X" ) );

		await Assert.ThrowsAsync<ArgumentException>( () => _admin.EnableCategoryAsync( code, retiring ) );
		Assert.Equal( ( code, retiring + "X" ), ( unknown.Country, unknown.Category ) );
	}

	[Fact]
	public async Task ALocalityNeedsAnAssignedCategory()
	{
		string code = await SeedAsync();
		string unassigned = NewCategory();

		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = unassigned, Name = "Unassigned" } );

		UnknownCategoryException unknown = await Assert.ThrowsAsync<UnknownCategoryException>(
			() => _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2001", "Haymarket", unassigned ) ) );
		ArgumentException missing = await Assert.ThrowsAsync<ArgumentException>(
			() => _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2001", "Haymarket", " " ) ) );
		Versioned<Locality> created = await _admin.CreateLocalityAsync( LocalityOf( code, "NSW", "2001", "Haymarket", " SUBURB " ) );

		Assert.Equal( ( code, unassigned ), ( unknown.Country, unknown.Category ) );
		Assert.Equal( nameof(LocalityDefinition.Category), missing.ParamName );
		Assert.Equal( "SUBURB", created.Value.Definition.Category );
		Assert.Equal( "SUBURB", ChangesOf( code ).Last().Category );
	}

	[Fact]
	public async Task ImportsCheckAndApplyTheRowsCategories()
	{
		string code = await SeedAsync();

		Task<ImportRun> Import( string records ) =>
			_admin.ImportAsync( code, new ImportRequest { Importer = "csv" },
				new MemoryStream( Encoding.UTF8.GetBytes( "subdivision,postcode,locality,category\n" + records ) ) );

		DatasetImportException missing = await Assert.ThrowsAsync<DatasetImportException>( () => Import( "NSW,2000,Sydney,\n" ) );
		DatasetImportException unknown = await Assert.ThrowsAsync<DatasetImportException>( () => Import( "NSW,2000,Sydney,TOWN\nACT,2620,Hume,NOPE\n" ) );
		DatasetImportException conflict = await Assert.ThrowsAsync<DatasetImportException>(
			() => Import( "NSW,2000,Sydney,TOWN\nNSW,2000,sydney,SUBURB\n" ) );

		Assert.Equal( 1, missing.Position );
		Assert.Equal( 2, unknown.Position );
		Assert.Equal( 2, conflict.Position );

		await _admin.RetireLocalityAsync( code, "ACT", "2620", "Hume" );

		// Sydney stays a TOWN, Hume (retired) is reinstated as a SUBURB, Parkes is new.
		ImportRun first = await Import( "NSW,2000,Sydney,TOWN\nACT,2620,Hume,SUBURB\nACT,2600,Parkes,SUBURB\nNSW,2000,Sydney,TOWN\n" );
		ImportRun second = await Import( "NSW,2000,Sydney,SUBURB\nACT,2620,Hume,SUBURB\nACT,2600,Parkes,SUBURB\n" );

		Assert.Equal( ( 1, 1, 0, 1, 0 ), ( first.Added, first.Reinstated, first.Recategorized, first.Unchanged, first.Deleted ) );
		Assert.Equal( ( 0, 0, 1, 2, 0 ), ( second.Added, second.Reinstated, second.Recategorized, second.Unchanged, second.Deleted ) );
		Assert.Equal( new[] { "Hume=SUBURB", "Parkes=SUBURB", "Sydney=SUBURB" }, CategoriesOf( ( await _admin.GetLocalitiesAsync( code ) ).Value ) );
		Assert.Equal( second.Recategorized, ( await _admin.GetImportRunsAsync( code ) ).First().Recategorized );
		Assert.Equal( new[] { "Sydney" }, ( await _admin.SearchLocalitiesAsync( code, "s", category: "SUBURB" ) ).Value.Select( l => l.Definition.Name ) );
	}

	[Fact]
	public async Task CountriesAreListedByCategoryWithTheirStateAndAuditContexts()
	{
		string category = NewCategory();
		string first = await SeedAsync(), second = await SeedAsync();
		await SeedAsync();   // a country without the category, which the listing leaves out

		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = category, Name = "Listed" } );
		Versioned<CountryCategory> assigned = await _admin.AssignCategoryAsync( first, category );
		await _admin.AssignCategoryAsync( second, category );
		Versioned<CountryCategory> disabled = await _admin.DisableCategoryAsync( second, category );
		await _admin.RetireCountryAsync( second );

		CountryCategory[] all = ( await _admin.GetCategoryCountriesAsync( $" {category} " ) ).ToArray();
		CountryCategory[] enabled = ( await _admin.GetCategoryCountriesAsync( category, includeDisabled: false ) ).ToArray();

		Assert.Equal( new[] { first, second }, all.Select( c => c.Country ) );
		Assert.All( all, c => Assert.Equal( ( category, "Listed" ), ( c.Category, c.Name ) ) );
		Assert.Equal( ( (DateTime?)null, assigned.DatasetVersion, "alice", (DateTime?)null ),
					( all[ 0 ].Disabled, all[ 0 ].Registration.Timestamp, all[ 0 ].Registration.Actor.Identifier, all[ 0 ].LastUpdate?.Timestamp ) );
		Assert.Equal( ( disabled.DatasetVersion, disabled.DatasetVersion ), ( all[ 1 ].Disabled, all[ 1 ].LastUpdate!.Timestamp ) );
		Assert.Equal( new[] { first }, enabled.Select( c => c.Country ) );

		string lonely = NewCategory();
		await _admin.CreateCategoryAsync( new CategoryDefinition { Code = lonely, Name = "Lonely" } );

		Assert.Empty( await _admin.GetCategoryCountriesAsync( lonely ) );
		Assert.Null( ( await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.GetCategoryCountriesAsync( lonely + "X" ) ) ).Country );
		await Assert.ThrowsAsync<ArgumentException>( () => _admin.GetCategoryCountriesAsync( " " ) );
	}
}
