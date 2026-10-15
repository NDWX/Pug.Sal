using System.Text;
using Pug.Application.Security;
using Pug.Sal.Abstractions;
using Pug.Sal.Importers.Csv;
using Pug.Sal.Models;
using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

// ILocalityAdministration over a real store, run against every store: SqliteLocalityAdministrationTests
// here, PostgresLocalityAdministrationTests in Pug.Sal.Postgres.Tests.
public abstract class LocalityAdministrationContractTests<TFixture> : IClassFixture<TFixture> where TFixture : class, IStoreFixture
{
	private readonly TFixture _fixture;

	private readonly RecordingEventPublisher _eventPublisher = new();

	private readonly LocalityAdministration _admin;

	protected LocalityAdministrationContractTests( TFixture fixture )
	{
		_fixture = fixture;
		_admin = Admin();
	}

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
			HasSubdivisions = true
		};

	private static LocalityDefinition LocalityOf( string country, string? subdivision, string? postcode, string name ) =>
		new() { Country = country, Subdivision = subdivision!, Postcode = postcode!, Name = name };

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
			new ImportRequest { Importer = "CSV", Source = "test.csv" },
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
		{ SecurityOperations.Import, a => a.GetImportRunsAsync( "ZZ" ) }
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

		LocalityChange change = Assert.Single( ChangesOf( code ) );
		Assert.Equal( ( ChangeEntityTypes.Country, code, ChangeKinds.Created, country.CurrentVersion ),
					( change.EntityType, change.Key, change.Kind, change.DatasetVersion ) );
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
		await _admin.CreateCountryAsync( new CountryDefinition { Code = flat, Name = "Flat", HasSubdivisions = false } );
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

		Assert.Equal( 2, failing.Changes.Count );
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
		await _admin.CreateCountryAsync( new CountryDefinition { Code = flat, Name = "Flat", HasSubdivisions = false } );

		await Assert.ThrowsAsync<DatasetImportException>( () => ImportAsync( code ) );
		await Assert.ThrowsAsync<DatasetImportException>( () => ImportAsync( flat, "X,,Yaren" ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => ImportAsync( NewCountry(), "NSW,2000,Sydney" ) );
		await Assert.ThrowsAsync<UnknownDatasetImporterException>(
			() => _admin.ImportAsync( code, new ImportRequest { Importer = "xml" }, new MemoryStream() ) );
		Assert.Equal( 2, ( await LocalitiesAsync( code ) ).Length );
	}
}
