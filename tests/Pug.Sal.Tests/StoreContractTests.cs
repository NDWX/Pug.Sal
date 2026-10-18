using System.Data.Common;
using Pug.Effable;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;
using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

// The IDataSession contract, run against every store: SqliteStoreTests here, PostgresStoreTests in
// Pug.Sal.Postgres.Tests. Keeping the stores in step means both pass the whole suite.
public abstract class StoreContractTests<TFixture> : IClassFixture<TFixture> where TFixture : class, IStoreFixture
{
	private static readonly DateTime Base = new( 2026, 3, 1, 8, 30, 15, DateTimeKind.Utc );

	private readonly TFixture _fixture;

	protected StoreContractTests( TFixture fixture )
	{
		_fixture = fixture;
	}

	// Each test uses its own country code so tests sharing the fixture database stay independent.
	private static int _countryCounter;

	private static string NewCountry()
	{
		int n = Interlocked.Increment( ref _countryCounter );
		return $"{(char)( 'A' + n / 26 % 26 )}{(char)( 'A' + n % 26 )}";
	}

	private static ActionContext<Reference> Context( DateTime timestamp, string actor = "alice" ) =>
		new() { Actor = new Reference { Type = "USER", Identifier = actor }, Timestamp = timestamp };

	private static Country NewCountryRecord( string code, bool subdivisions = true, DateTime? version = null ) =>
		new()
		{
			Definition = new CountryDefinition
			{
				Code = code,
				Alpha3 = subdivisions ? code + "X" : null!,
				Name = "Australia " + code,
				PostcodePattern = subdivisions ? @"^\d{4}$" : null!,
				PostcodeRequired = subdivisions,
				HasSubdivisions = subdivisions
			},
			CurrentVersion = version ?? Base,
			Registration = Context( Base )
		};

	private static Subdivision NewSubdivisionRecord( string country, string code, string name, DateTime? retired = null ) =>
		new()
		{
			Definition = new SubdivisionDefinition { Country = country, Code = code, Name = name, Type = SubdivisionTypes.State },
			Retired = retired,
			Registration = Context( Base )
		};

	private static Locality NewLocalityRecord( string country, string? subdivision, string? postcode, string name,
										DateTime? registered = null, DateTime? retired = null, string category = "TOWN" ) =>
		new()
		{
			Definition = new LocalityDefinition
			{
				Country = country,
				Subdivision = subdivision!,
				Postcode = postcode!,
				Name = name,
				Category = category
			},
			Retired = retired,
			Registration = Context( registered ?? Base )
		};

	// Test keys: upper-cased name, postcode as given.
	private static string Key( string name ) => name.ToUpperInvariant();

	// The leading prefix matches of a search. A store with similarity matching (Postgres) may follow
	// them with close, non-prefix matches (DECISIONS D3), which must all come after.
	private static Locality[] PrefixMatches( IEnumerable<Locality> results, string prefixKey )
	{
		Locality[] all = results.ToArray();
		Locality[] prefixed = all.TakeWhile( l => Key( l.Definition.Name ).StartsWith( prefixKey, StringComparison.Ordinal ) ).ToArray();

		Assert.DoesNotContain( all.Skip( prefixed.Length ), l => Key( l.Definition.Name ).StartsWith( prefixKey, StringComparison.Ordinal ) );

		return prefixed;
	}

	private static LocalityKey KeyOf( Locality locality ) =>
		new( locality.Definition.Country, locality.Definition.Subdivision, locality.Definition.Postcode, Key( locality.Definition.Name ) );

	private Task InsertAsync( Locality locality ) =>
		_fixture.ApplicationData.WriteAsync(
				async ( session, context ) =>
				{
					await session.InsertLocalityAsync( context.locality, context.locality.Definition.Postcode, Key( context.locality.Definition.Name ) );

					return true;
				},
				context: new { locality } );

	private Task<T> ReadAsync<T>( Func<IDataSession, Task<T>> read ) =>
		_fixture.ApplicationData.ReadAsync( async ( session, context ) => await read( session ), context: new { } );

	private Task WriteAsync( Func<IDataSession, Task> write ) =>
		_fixture.ApplicationData.WriteAsync(
			async ( session, context ) =>
			{
				await write( session );

				return true;
			},
			context: new { } );

	private async Task<(string Country, string[] Names)> SeedAustraliaAsync()
	{
		string code = NewCountry();

		await WriteAsync( async session =>
		{
			await session.InsertCountryAsync( NewCountryRecord( code ) );
			await session.InsertSubdivisionAsync( NewSubdivisionRecord( code, "NSW", "New South Wales" ) );
			await session.InsertSubdivisionAsync( NewSubdivisionRecord( code, "ACT", "Australian Capital Territory" ) );
			await session.InsertSubdivisionAsync( NewSubdivisionRecord( code, "QLD", "Queensland", retired: Base.AddDays( 1 ) ) );
		} );

		await InsertAsync( NewLocalityRecord( code, "NSW", "2000", "Sydney" ) );
		await InsertAsync( NewLocalityRecord( code, "ACT", "2600", "Parkes" ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2620", "Environa" ) );
		await InsertAsync( NewLocalityRecord( code, "ACT", "2620", "Hume" ) );
		await InsertAsync( NewLocalityRecord( code, "QLD", "4000", "Spring Creek" ) );
		await InsertAsync( NewLocalityRecord( code, "QLD", "4370", "Spring Creek" ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2001", "Sydney Retired", retired: Base.AddHours( 2 ) ) );

		return ( code, Array.Empty<string>() );
	}

	[Fact]
	public async Task SchemaMigrates()
	{
		IEnumerable<Country> countries = await ReadAsync( s => s.GetCountriesAsync( true ) );

		Assert.NotNull( countries );
	}

	[Fact]
	public async Task CountryRoundTripsEveryField()
	{
		string code = NewCountry();
		DateTime version = Base.AddTicks( 1230 );
		Country country = NewCountryRecord( code, version: version ) with
		{
			Retired = Base.AddDays( 3 ).AddTicks( 70 ),
			LastUpdate = Context( Base.AddDays( 2 ).AddTicks( 10 ), "bob" )
		};

		await WriteAsync( s => s.InsertCountryAsync( country ) );
		Country? read = await ReadAsync( s => s.GetCountryAsync( code ) );

		Assert.NotNull( read );
		Assert.Equal( country.Definition, read.Definition );
		Assert.Equal( version, read.CurrentVersion );
		Assert.Equal( DateTimeKind.Utc, read.CurrentVersion.Kind );
		Assert.Equal( country.Retired, read.Retired );
		Assert.Equal( DateTimeKind.Utc, read.Retired!.Value.Kind );
		AssertContext( country.Registration, read.Registration );
		AssertContext( country.LastUpdate, read.LastUpdate );
	}

	private static void AssertContext( ActionContext<Reference> expected, ActionContext<Reference> actual )
	{
		Assert.Equal( expected.Actor.Type, actual.Actor.Type );
		Assert.Equal( expected.Actor.Identifier, actual.Actor.Identifier );
		Assert.Equal( expected.Timestamp, actual.Timestamp );
		Assert.Equal( DateTimeKind.Utc, actual.Timestamp.Kind );
	}

	[Fact]
	public async Task CountryWithoutSubdivisionsOrPostcodesRoundTripsNulls()
	{
		string code = NewCountry();
		Country country = NewCountryRecord( code, subdivisions: false );

		await WriteAsync( s => s.InsertCountryAsync( country ) );
		Country? read = await ReadAsync( s => s.GetCountryAsync( code ) );

		Assert.Equal( country.Definition, read!.Definition );
		Assert.Null( read.Definition.PostcodePattern );
		Assert.Null( read.Definition.Alpha3 );
		Assert.Null( read.Retired );
		Assert.Null( read.LastUpdate );
	}

	[Fact]
	public async Task UnknownCountryIsNull()
	{
		Assert.Null( await ReadAsync( s => s.GetCountryAsync( "ZZ" ) ) );
	}

	[Fact]
	public async Task CountriesListFiltersRetiredAndOrdersByCode()
	{
		string a = NewCountry();
		string b = NewCountry();
		await WriteAsync( async s =>
		{
			await s.InsertCountryAsync( NewCountryRecord( b ) with { Retired = Base } );
			await s.InsertCountryAsync( NewCountryRecord( a ) );
		} );

		string[] current = ( await ReadAsync( s => s.GetCountriesAsync( false ) ) ).Select( c => c.Definition.Code ).ToArray();
		string[] all = ( await ReadAsync( s => s.GetCountriesAsync( true ) ) ).Select( c => c.Definition.Code ).ToArray();
		CountryVersion[] versions = ( await ReadAsync( s => s.GetCountryVersionsAsync() ) ).ToArray();

		Assert.Contains( a, current );
		Assert.DoesNotContain( b, current );
		Assert.Contains( b, all );
		Assert.Equal( all.OrderBy( c => c, StringComparer.Ordinal ), all );
		Assert.Equal( all, versions.Select( v => v.Country ) );
		Assert.All( versions, v => Assert.Equal( DateTimeKind.Utc, v.Version.Kind ) );
	}

	[Fact]
	public async Task UpdateAndRetireCountry()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code ) ) );
		CountryDefinition changed = NewCountryRecord( code ).Definition with { Name = "Renamed", PostcodePattern = @"^\d{5}$" };
		ActionContext<Reference> update = Context( Base.AddDays( 1 ), "bob" );
		DateTime retired = Base.AddDays( 2 );

		bool updated = false, retiredOk = false, unknown = true, unknownRetire = true;
		await WriteAsync( async s =>
		{
			updated = await s.UpdateCountryAsync( changed, update );
			retiredOk = await s.SetCountryRetiredAsync( code, retired, update );
			unknown = await s.UpdateCountryAsync( changed with { Code = "ZZ" }, update );
			unknownRetire = await s.SetCountryRetiredAsync( "ZZ", retired, update );
		} );
		Country? read = await ReadAsync( s => s.GetCountryAsync( code ) );

		Assert.True( updated );
		Assert.True( retiredOk );
		Assert.False( unknown );
		Assert.False( unknownRetire );
		Assert.Equal( changed, read!.Definition );
		Assert.Equal( retired, read.Retired );
		AssertContext( update, read.LastUpdate );

		await WriteAsync( s => s.SetCountryRetiredAsync( code, null, update ) );
		Assert.Null( ( await ReadAsync( s => s.GetCountryAsync( code ) ) )!.Retired );
	}

	[Fact]
	public async Task SubdivisionRoundTripsListsAndUpdates()
	{
		( string code, _ ) = await SeedAustraliaAsync();

		Subdivision? nsw = await ReadAsync( s => s.GetSubdivisionAsync( code, "NSW" ) );
		Subdivision[] current = ( await ReadAsync( s => s.GetSubdivisionsAsync( code, false ) ) ).ToArray();
		Subdivision[] all = ( await ReadAsync( s => s.GetSubdivisionsAsync( code, true ) ) ).ToArray();

		Assert.Equal( "New South Wales", nsw!.Definition.Name );
		Assert.Equal( SubdivisionTypes.State, nsw.Definition.Type );
		Assert.Null( nsw.LastUpdate );
		Assert.Equal( new[] { "ACT", "NSW" }, current.Select( s => s.Definition.Code ) );
		Assert.Equal( new[] { "ACT", "NSW", "QLD" }, all.Select( s => s.Definition.Code ) );
		Assert.Equal( Base.AddDays( 1 ), all[2].Retired );
		Assert.Equal( DateTimeKind.Utc, all[2].Retired!.Value.Kind );

		ActionContext<Reference> update = Context( Base.AddDays( 5 ), "bob" );
		bool updated = false, retired = false, unknownUpdate = true, unknownRetire = true;
		await WriteAsync( async s =>
		{
			updated = await s.UpdateSubdivisionAsync( nsw.Definition with { Name = "NSW", Type = "TERRITORY" }, update );
			retired = await s.SetSubdivisionRetiredAsync( code, "ACT", Base.AddDays( 6 ), update );
			unknownUpdate = await s.UpdateSubdivisionAsync( nsw.Definition with { Code = "XXX" }, update );
			unknownRetire = await s.SetSubdivisionRetiredAsync( code, "XXX", null, update );
		} );
		Subdivision? after = await ReadAsync( s => s.GetSubdivisionAsync( code, "NSW" ) );
		Subdivision? act = await ReadAsync( s => s.GetSubdivisionAsync( code, "ACT" ) );

		Assert.True( updated );
		Assert.True( retired );
		Assert.False( unknownUpdate );
		Assert.False( unknownRetire );
		Assert.Equal( "NSW", after!.Definition.Name );
		Assert.Equal( "TERRITORY", after.Definition.Type );
		AssertContext( update, after.LastUpdate );
		Assert.Equal( Base.AddDays( 6 ), act!.Retired );
		Assert.Null( await ReadAsync( s => s.GetSubdivisionAsync( code, "XXX" ) ) );
	}

	[Fact]
	public async Task LocalityRoundTripsEveryField()
	{
		( string code, _ ) = await SeedAustraliaAsync();
		Locality locality = NewLocalityRecord( code, "NSW", "2650", "Wagga Wagga", registered: Base.AddTicks( 4560 ) ) with
		{
			Retired = Base.AddDays( 1 ).AddTicks( 80 ),
			LastUpdate = Context( Base.AddDays( 2 ).AddTicks( 120 ), "bob" )
		};

		await InsertAsync( locality );
		Locality? read = await ReadAsync( s => s.GetLocalityAsync( KeyOf( locality ) ) );

		Assert.NotNull( read );
		Assert.Equal( locality.Definition, read.Definition );
		Assert.Equal( locality.Retired, read.Retired );
		Assert.Equal( DateTimeKind.Utc, read.Retired!.Value.Kind );
		AssertContext( locality.Registration, read.Registration );
		AssertContext( locality.LastUpdate, read.LastUpdate );
	}

	[Fact]
	public async Task LocalityWithoutSubdivisionOrPostcodeRoundTripsNulls()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code, subdivisions: false ) ) );
		Locality locality = NewLocalityRecord( code, null, null, "Nauru" );

		await WriteAsync( s => s.InsertLocalityAsync( locality, null, "NAURU" ) );
		Locality? read = await ReadAsync( s => s.GetLocalityAsync( new LocalityKey( code, null, null, "NAURU" ) ) );
		Locality[] listed = ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, null, null, false ) ) ).ToArray();

		Assert.NotNull( read );
		Assert.Null( read.Definition.Subdivision );
		Assert.Null( read.Definition.Postcode );
		Assert.Single( listed );
		Assert.Null( listed[0].Definition.Subdivision );
		Assert.True( await ReadAsync( s => s.DeleteLocalityAsync( new LocalityKey( code, null, null, "NAURU" ) ) ) );
	}

	[Fact]
	public async Task PostcodeIsSharedAcrossSubdivisionsAndNamesAreToldApartByPostcode()
	{
		( string code, _ ) = await SeedAustraliaAsync();

		Locality[] shared = ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, "2620", null, null, false ) ) ).ToArray();
		Locality[] springs = ( await ReadAsync( s => s.GetLocalitiesAsync( code, "QLD", null, "SPRING CREEK", null, false ) ) ).ToArray();

		Assert.Equal( new[] { "ACT", "NSW" }, shared.Select( l => l.Definition.Subdivision ) );
		Assert.Equal( new[] { "4000", "4370" }, springs.Select( l => l.Definition.Postcode ) );
	}

	[Fact]
	public async Task LocalityListFiltersAndOrders()
	{
		( string code, _ ) = await SeedAustraliaAsync();

		Locality[] current = ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, null, null, false ) ) ).ToArray();
		Locality[] all = ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, null, null, true ) ) ).ToArray();
		Locality[] act = ( await ReadAsync( s => s.GetLocalitiesAsync( code, "ACT", null, null, null, false ) ) ).ToArray();
		Locality[] byName = ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, "SYDNEY", null, false ) ) ).ToArray();
		Locality[] other = ( await ReadAsync( s => s.GetLocalitiesAsync( NewCountry(), null, null, null, null, true ) ) ).ToArray();

		Assert.Equal( 6, current.Length );
		Assert.Equal( 7, all.Length );
		Assert.Equal( new[] { "Parkes", "Hume" }.OrderBy( n => Key( n ), StringComparer.Ordinal ), act.Select( l => l.Definition.Name ) );
		Assert.Equal( "2000", Assert.Single( byName ).Definition.Postcode );
		Assert.Empty( other );
		// Ordered by subdivision, name key, postcode key.
		Assert.Equal(
				all.OrderBy( l => l.Definition.Subdivision, StringComparer.Ordinal )
					.ThenBy( l => Key( l.Definition.Name ), StringComparer.Ordinal )
					.ThenBy( l => l.Definition.Postcode, StringComparer.Ordinal ).Select( l => l.Definition ),
				all.Select( l => l.Definition ) );
	}

	[Fact]
	public async Task SearchMatchesPrefixOrdersLimitsAndExcludesRetired()
	{
		( string code, _ ) = await SeedAustraliaAsync();

		Locality[] sy = PrefixMatches( await ReadAsync( s => s.SearchLocalitiesAsync( code, "SY", null, null, 10 ) ), "SY" );
		Locality[] s = PrefixMatches( await ReadAsync( x => x.SearchLocalitiesAsync( code, "S", null, null, 10 ) ), "S" );
		Locality[] limited = ( await ReadAsync( x => x.SearchLocalitiesAsync( code, "S", null, null, 2 ) ) ).ToArray();
		Locality[] qld = PrefixMatches( await ReadAsync( x => x.SearchLocalitiesAsync( code, "S", "QLD", null, 10 ) ), "S" );
		Locality[] none = PrefixMatches( await ReadAsync( x => x.SearchLocalitiesAsync( code, "ZZ", null, null, 10 ) ), "ZZ" );

		Assert.Equal( "Sydney", Assert.Single( sy ).Definition.Name );
		Assert.Equal( new[] { "Spring Creek", "Spring Creek", "Sydney" }, s.Select( l => l.Definition.Name ) );
		Assert.Equal( new[] { "4000", "4370" }, s.Take( 2 ).Select( l => l.Definition.Postcode ) );
		Assert.Equal( 2, limited.Length );
		Assert.Equal( 2, qld.Length );
		Assert.Empty( none );
		Assert.DoesNotContain( s, l => l.Definition.Name == "Sydney Retired" );
	}

	[Fact]
	public async Task SearchTreatsWildcardsLiterally()
	{
		( string code, _ ) = await SeedAustraliaAsync();
		await InsertAsync( NewLocalityRecord( code, "NSW", "2002", "50% Off_Town" ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2003", "50X Offxtown" ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2004", @"A\B" ) );

		Locality[] percent = PrefixMatches( await ReadAsync( s => s.SearchLocalitiesAsync( code, "50%", null, null, 10 ) ), "50%" );
		Locality[] underscore = PrefixMatches( await ReadAsync( s => s.SearchLocalitiesAsync( code, "50% OFF_", null, null, 10 ) ), "50% OFF_" );
		Locality[] wild = PrefixMatches( await ReadAsync( s => s.SearchLocalitiesAsync( code, "%", null, null, 10 ) ), "%" );
		Locality[] slash = PrefixMatches( await ReadAsync( s => s.SearchLocalitiesAsync( code, @"A\", null, null, 10 ) ), @"A\" );

		Assert.Equal( "50% Off_Town", Assert.Single( percent ).Definition.Name );
		Assert.Equal( "50% Off_Town", Assert.Single( underscore ).Definition.Name );
		Assert.Empty( wild );
		Assert.Equal( @"A\B", Assert.Single( slash ).Definition.Name );
	}

	[Fact]
	public async Task DuplicateKeysThrow()
	{
		( string code, _ ) = await SeedAustraliaAsync();

		await Assert.ThrowsAsync<DuplicateKeyException>( () => InsertAsync( NewLocalityRecord( code, "NSW", "2000", "SYDNEY" ) ) );
		await Assert.ThrowsAsync<DuplicateKeyException>( () => WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code ) ) ) );
		await Assert.ThrowsAsync<DuplicateKeyException>( () => WriteAsync( s => s.InsertSubdivisionAsync( NewSubdivisionRecord( code, "NSW", "Again" ) ) ) );
	}

	[Fact]
	public async Task HasLocalitiesCountsRetiredOnes()
	{
		string empty = NewCountry();
		string retiredOnly = NewCountry();

		await WriteAsync( async s =>
		{
			await s.InsertCountryAsync( NewCountryRecord( empty, subdivisions: false ) );
			await s.InsertCountryAsync( NewCountryRecord( retiredOnly, subdivisions: false ) );
		} );
		await InsertAsync( NewLocalityRecord( retiredOnly, null, null, "Gone", retired: Base ) );

		Assert.False( await ReadAsync( s => s.HasLocalitiesAsync( empty ) ) );
		Assert.True( await ReadAsync( s => s.HasLocalitiesAsync( retiredOnly ) ) );
	}

	[Fact]
	public async Task LocalityUnderUnknownCountryIsRejected()
	{
		await Assert.ThrowsAnyAsync<DbException>( () => InsertAsync( NewLocalityRecord( "QQ", null, null, "Nowhere" ) ) );
	}

	[Fact]
	public async Task LocalityRetireReinstateAndDelete()
	{
		( string code, _ ) = await SeedAustraliaAsync();
		LocalityKey key = new( code, "NSW", "2000", "SYDNEY" );
		LocalityKey unknown = new( code, "NSW", "9999", "NOWHERE" );
		ActionContext<Reference> retire = Context( Base.AddDays( 1 ), "bob" );
		ActionContext<Reference> reinstate = Context( Base.AddDays( 2 ), "carol" );

		bool retired = await ReadAsync( s => s.SetLocalityRetiredAsync( key, retire.Timestamp, retire ) );
		bool unknownRetire = true, unknownDelete = true;
		await WriteAsync( async s =>
		{
			unknownRetire = await s.SetLocalityRetiredAsync( unknown, null, retire );
			unknownDelete = await s.DeleteLocalityAsync( unknown );
		} );
		Locality? afterRetire = await ReadAsync( s => s.GetLocalityAsync( key ) );
		await WriteAsync( s => s.SetLocalityRetiredAsync( key, null, reinstate ) );
		Locality? afterReinstate = await ReadAsync( s => s.GetLocalityAsync( key ) );
		bool deleted = false;
		await WriteAsync( async s => deleted = await s.DeleteLocalityAsync( key ) );

		Assert.True( retired );
		Assert.False( unknownRetire );
		Assert.False( unknownDelete );
		Assert.Equal( retire.Timestamp, afterRetire!.Retired );
		AssertContext( retire, afterRetire.LastUpdate );
		Assert.Null( afterReinstate!.Retired );
		AssertContext( reinstate, afterReinstate.LastUpdate );
		Assert.True( deleted );
		Assert.Null( await ReadAsync( s => s.GetLocalityAsync( key ) ) );

		// SetLocalityRetired moves the stamped time to lastUpdate's timestamp.
		Assert.Equal( 0, await ReadAsync( s => s.DeleteLocalitiesStampedBeforeAsync( "ZZ", Base ) ) );
	}

	[Fact]
	public async Task AdvanceVersionIsStrictlyIncreasing()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code, version: Base ) ) );
		List<DateTime> versions = new();

		foreach( DateTime now in new[] { Base.AddSeconds( 10 ), Base.AddSeconds( 10 ), Base.AddSeconds( 3 ), Base.AddSeconds( 3 ) } )
			await WriteAsync( async s => versions.Add( ( await s.AdvanceVersionAsync( code, now ) )!.Value ) );

		Assert.Equal( Base.AddSeconds( 10 ), versions[0] );
		Assert.Equal( versions[0].AddTicks( 1 ), versions[1] );
		Assert.Equal( versions[1].AddTicks( 1 ), versions[2] );
		Assert.Equal( versions[2].AddTicks( 1 ), versions[3] );
		Assert.All( versions, v => Assert.Equal( DateTimeKind.Utc, v.Kind ) );
		Country? read = await ReadAsync( s => s.GetCountryAsync( code ) );
		Assert.Equal( versions[3], read!.CurrentVersion );
		Assert.Equal( DateTimeKind.Utc, read.CurrentVersion.Kind );
	}

	[Fact]
	public async Task VersionColumnsAreStoredAsTickExactDigitText()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code, version: Base ) ) );
		// Odd ticks, and fractions whose default text would differ in length: .0000001, .1, .1000001, .12.
		DateTime[] nows = { Base.AddTicks( 1 ), Base.AddTicks( 1_000_000 ), Base.AddTicks( 1_000_001 ), Base.AddTicks( 1_200_000 ), Base.AddSeconds( 1 ) };

		foreach( DateTime now in nows )
		{
			DateTime? version = null;
			await WriteAsync( async s => version = await s.AdvanceVersionAsync( code, now ) );
			Assert.Equal( now, version );
			Assert.Equal( now, ( await ReadAsync( s => s.GetCountryAsync( code ) ) )!.CurrentVersion );
		}

		string stored = Assert.Single( await _fixture.QueryTextAsync( "select currentVersion from country where code = @code;", new { code } ) );
		Assert.Equal( "202603010830160000000", stored );

		// stamped is digit text too, so the stale-row comparison is exact to the tick.
		await InsertAsync( NewLocalityRecord( code, "NSW", "2000", "Early", registered: Base ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2000", "Tick1", registered: Base.AddTicks( 1 ) ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2000", "Frac1", registered: Base.AddTicks( 1_000_000 ) ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2000", "Frac2", registered: Base.AddTicks( 1_000_001 ) ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2000", "Later", registered: Base.AddSeconds( 1 ) ) );
		string[] stamps = await _fixture.QueryTextAsync( "select stamped from locality where country = @code order by stamped;", new { code } );
		Assert.Equal(
				new[] { Base, Base.AddTicks( 1 ), Base.AddTicks( 1_000_000 ), Base.AddTicks( 1_000_001 ), Base.AddSeconds( 1 ) }
					.Select( DatasetVersions.ToText ),
				stamps );

		int deleted = await ReadAsync( s => s.DeleteLocalitiesStampedBeforeAsync( code, Base.AddTicks( 1 ) ) );
		Assert.Equal( 1, deleted );
		deleted = await ReadAsync( s => s.DeleteLocalitiesStampedBeforeAsync( code, Base.AddTicks( 1_000_001 ) ) );
		Assert.Equal( 2, deleted );
	}

	[Fact]
	public async Task AdvanceVersionOfUnknownCountryIsNull()
	{
		DateTime? version = DateTime.MinValue;
		await WriteAsync( async s => version = await s.AdvanceVersionAsync( "ZZ", Base ) );

		Assert.Null( version );
	}

	[Fact]
	public async Task ConcurrentTransactionsGetDistinctVersions()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code, version: Base ) ) );

		// Same utcNow for everyone, so only the serialization gives distinct values.
		Task<DateTime?>[] tasks = Enumerable.Range( 0, 6 ).Select( _ =>
			Task.Run( () => _fixture.ApplicationData.WriteAsync(
							async ( session, context ) =>
							{
								DateTime? version = await session.AdvanceVersionAsync( context.code, context.now );
								await Task.Delay( 20 );
								return version;
							},
							context: new { code, now = Base.AddSeconds( 1 ) } ) ) ).ToArray();

		DateTime?[] versions = await Task.WhenAll( tasks );

		Assert.Equal( 6, versions.Distinct().Count() );
		Assert.Equal( versions.Max(), ( await ReadAsync( s => s.GetCountryAsync( code ) ) )!.CurrentVersion );
	}

	// A read unit of work takes no write lock (DECISIONS D19): a write to the same country completes
	// while a read is open, and the read keeps seeing the snapshot it started with.
	[Fact]
	public async Task AnOpenReadDoesNotBlockAWrite()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code, version: Base ) ) );

		TaskCompletionSource readStarted = new( TaskCreationOptions.RunContinuationsAsynchronously );
		TaskCompletionSource writeDone = new( TaskCreationOptions.RunContinuationsAsynchronously );

		Task<( DateTime? before, DateTime? after )> read = Task.Run( () => _fixture.ApplicationData.ReadAsync(
						async ( session, context ) =>
						{
							DateTime? before = ( await session.GetCountryAsync( context ) )?.CurrentVersion;
							readStarted.SetResult();
							await writeDone.Task;

							return ( before, ( await session.GetCountryAsync( context ) )?.CurrentVersion );
						},
						code ) );

		try
		{
			await readStarted.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );

			await Task.Run( () => _fixture.ApplicationData.WriteAsync(
							( session, context ) => session.AdvanceVersionAsync( context, Base.AddDays( 1 ) ),
							code ) )
						.WaitAsync( TimeSpan.FromSeconds( 5 ) );
		}
		finally
		{
			writeDone.TrySetResult();
		}

		( DateTime? before, DateTime? after ) = await read;

		Assert.Equal( Base, before );
		Assert.Equal( Base, after );
		Assert.Equal( Base.AddDays( 1 ), ( await ReadAsync( s => s.GetCountryAsync( code ) ) )!.CurrentVersion );
	}

	[Fact]
	public async Task FailedUnitOfWorkRollsBackEveryWrite()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code, version: Base ) ) );

		await Assert.ThrowsAsync<InvalidOperationException>( () => WriteAsync( async s =>
		{
			await s.AdvanceVersionAsync( code, Base.AddDays( 1 ) );
			await s.InsertSubdivisionAsync( NewSubdivisionRecord( code, "NSW", "New South Wales" ) );
			throw new InvalidOperationException( "boom" );
		} ) );

		Assert.Equal( Base, ( await ReadAsync( s => s.GetCountryAsync( code ) ) )!.CurrentVersion );
		Assert.Empty( await ReadAsync( s => s.GetSubdivisionsAsync( code, true ) ) );
	}

	[Fact]
	public async Task UpsertReportsAddedReinstatedAndUnchangedAndStamps()
	{
		( string code, _ ) = await SeedAustraliaAsync();
		DateTime importAt = Base.AddDays(10).AddTicks( 70 );
		ActionContext<Reference> import = Context( importAt, "importer" );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2650", "Wagga Wagga", retired: Base.AddDays( 1 ) ) );
		LocalityUpsertOutcome added = default, reinstated = default, unchanged = default;

		await WriteAsync( async s =>
		{
			added = await s.UpsertImportedLocalityAsync( NewLocalityRecord( code, "NSW", "2800", "Orange" ), "2800", "ORANGE", import );
			reinstated = await s.UpsertImportedLocalityAsync( NewLocalityRecord( code, "NSW", "2650", "Wagga Wagga" ), "2650", "WAGGA WAGGA", import );
			unchanged = await s.UpsertImportedLocalityAsync( NewLocalityRecord( code, "NSW", "2000", "Sydney" ), "2000", "SYDNEY", import );
		} );

		Assert.Equal( LocalityUpsertOutcome.Added, added );
		Assert.Equal( LocalityUpsertOutcome.Reinstated, reinstated );
		Assert.Equal( LocalityUpsertOutcome.Unchanged, unchanged );

		Locality orange = ( await ReadAsync( s => s.GetLocalityAsync( new LocalityKey( code, "NSW", "2800", "ORANGE" ) ) ) )!;
		Locality wagga = ( await ReadAsync( s => s.GetLocalityAsync( new LocalityKey( code, "NSW", "2650", "WAGGA WAGGA" ) ) ) )!;
		Locality sydney = ( await ReadAsync( s => s.GetLocalityAsync( new LocalityKey( code, "NSW", "2000", "SYDNEY" ) ) ) )!;
		AssertContext( import, orange.Registration );
		Assert.Null( orange.LastUpdate );
		Assert.Null( wagga.Retired );
		AssertContext( import, wagga.LastUpdate );
		Assert.Equal( Base, wagga.Registration.Timestamp );
		Assert.Null( sydney.Retired );
		AssertContext( import, sydney.LastUpdate );
	}

	[Fact]
	public async Task DeleteStampedBeforeDeletesOnlyUnconfirmedRowsOfThatCountry()
	{
		( string code, _ ) = await SeedAustraliaAsync();
		( string otherCode, _ ) = await SeedAustraliaAsync();
		DateTime importAt = Base.AddDays( 10 );
		ActionContext<Reference> import = Context( importAt );
		int deleted = 0;

		// Sydney is confirmed (old Registration, new LastUpdate), Orange is new, the rest untouched.
		await WriteAsync( async s =>
		{
			await s.UpsertImportedLocalityAsync( NewLocalityRecord( code, "NSW", "2000", "Sydney" ), "2000", "SYDNEY", import );
			await s.UpsertImportedLocalityAsync( NewLocalityRecord( code, "NSW", "2800", "Orange" ), "2800", "ORANGE", import );
			deleted = await s.DeleteLocalitiesStampedBeforeAsync( code, importAt );
		} );

		string[] remaining = ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, null, null, true ) ) )
								.Select( l => l.Definition.Name ).ToArray();
		int otherCount = ( await ReadAsync( s => s.GetLocalitiesAsync( otherCode, null, null, null, null, true ) ) ).Count();

		Assert.Equal( 6, deleted );
		Assert.Equal( new[] { "Orange", "Sydney" }, remaining.OrderBy( n => n, StringComparer.Ordinal ) );
		Assert.Equal( 7, otherCount );
	}

	[Fact]
	public async Task ImportRunsRoundTripMostRecentFirst()
	{
		string code = NewCountry();
		await WriteAsync( s => s.InsertCountryAsync( NewCountryRecord( code ) ) );
		ImportRun Run( string id, DateTime at ) =>
			new()
			{
				Identifier = id, Country = code, Importer = "csv", Source = id == "c" ? null! : "file-" + id,
				Added = 1, Reinstated = 2, Deleted = 3, Unchanged = 4,
				DatasetVersion = at, Performed = Context( at, "importer" )
			};

		await WriteAsync( async s =>
		{
			await s.InsertImportRunAsync( Run( "a", Base.AddDays( 1 ).AddTicks( 10 ) ) );
			await s.InsertImportRunAsync( Run( "b", Base.AddDays( 3 ) ) );
			await s.InsertImportRunAsync( Run( "c", Base.AddDays( 2 ) ) );
		} );
		ImportRun[] runs = ( await ReadAsync( s => s.GetImportRunsAsync( code ) ) ).ToArray();

		Assert.Equal( new[] { "b", "c", "a" }, runs.Select( r => r.Identifier ) );
		Assert.Empty( await ReadAsync( s => s.GetImportRunsAsync( "ZZ" ) ) );
		Assert.Equal( Run( "a", Base.AddDays( 1 ).AddTicks( 10 ) ).DatasetVersion, runs[2].DatasetVersion );
		Assert.Equal( DateTimeKind.Utc, runs[2].DatasetVersion.Kind );
		Assert.Null( runs[1].Source );
		Assert.Equal( "file-b", runs[0].Source );
		Assert.Equal( ( 1, 2, 3, 4 ), ( runs[0].Added, runs[0].Reinstated, runs[0].Deleted, runs[0].Unchanged ) );
		AssertContext( Context( Base.AddDays( 3 ), "importer" ), runs[0].Performed );
	}

	// == Categories (DECISIONS D28) ==

	[Fact]
	public async Task CategoriesAndCountryCategoriesRoundTrip()
	{
		string code = NewCountry();
		string first = "K1" + code, second = "K2" + code;

		await WriteAsync( async session =>
		{
			await session.InsertCountryAsync( NewCountryRecord( code ) );
			await session.InsertCategoryAsync( new Category { Definition = new CategoryDefinition { Code = first, Name = "First" }, Registration = Context( Base ) } );
			await session.InsertCategoryAsync( new Category
			{
				Definition = new CategoryDefinition { Code = second, Name = "Second" }, Retired = Base.AddTicks( 3 ), Registration = Context( Base )
			} );
			await session.InsertCountryCategoryAsync( new CountryCategory { Country = code, Category = second, Registration = Context( Base ) } );
			await session.InsertCountryCategoryAsync( new CountryCategory { Country = code, Category = first, Registration = Context( Base ) } );
		} );

		await Assert.ThrowsAsync<DuplicateKeyException>( () => WriteAsync( session =>
			session.InsertCountryCategoryAsync( new CountryCategory { Country = code, Category = first, Registration = Context( Base ) } ) ) );
		await Assert.ThrowsAsync<DuplicateKeyException>( () => WriteAsync( session =>
			session.InsertCategoryAsync( new Category { Definition = new CategoryDefinition { Code = first, Name = "Again" }, Registration = Context( Base ) } ) ) );

		Category? stored = await ReadAsync( s => s.GetCategoryAsync( second ) );
		Assert.Equal( ( "Second", Base.AddTicks( 3 ), DateTimeKind.Utc ), ( stored!.Definition.Name, stored.Retired, stored.Registration.Timestamp.Kind ) );
		Assert.DoesNotContain( await ReadAsync( s => s.GetCategoriesAsync( false ) ), c => c.Definition.Code == second );
		Assert.Contains( await ReadAsync( s => s.GetCategoriesAsync( true ) ), c => c.Definition.Code == second );

		DateTime disabled = Base.AddTicks( 7 );
		await WriteAsync( async session =>
		{
			Assert.True( await session.UpdateCategoryAsync( new CategoryDefinition { Code = first, Name = "Renamed" }, Context( Base.AddHours( 1 ) ) ) );
			Assert.True( await session.SetCountryCategoryDisabledAsync( code, second, disabled, Context( disabled ) ) );
			Assert.False( await session.SetCountryCategoryDisabledAsync( code, "NONE", disabled, Context( disabled ) ) );
		} );

		CountryCategory[] all = ( await ReadAsync( s => s.GetCountryCategoriesAsync( code, true ) ) ).ToArray();
		CountryCategory[] enabled = ( await ReadAsync( s => s.GetCountryCategoriesAsync( code, false ) ) ).ToArray();

		Assert.Equal( new[] { $"{first}|Renamed|", $"{second}|Second|{disabled.Ticks}" },
					all.Select( c => $"{c.Category}|{c.Name}|{c.Disabled?.Ticks}" ) );
		Assert.Equal( new[] { first }, enabled.Select( c => c.Category ) );
		Assert.Equal( disabled, ( await ReadAsync( s => s.GetCountryCategoryAsync( code, second ) ) )!.LastUpdate!.Timestamp );

		await WriteAsync( async session =>
		{
			Assert.True( await session.SetCategoryRetiredAsync( second, null, Context( Base.AddHours( 2 ) ) ) );
			Assert.True( await session.DeleteCountryCategoryAsync( code, second ) );
			Assert.False( await session.DeleteCountryCategoryAsync( code, second ) );
		} );

		Assert.Null( ( await ReadAsync( s => s.GetCategoryAsync( second ) ) )!.Retired );
		Assert.Null( await ReadAsync( s => s.GetCountryCategoryAsync( code, second ) ) );
	}

	[Fact]
	public async Task LocalitiesAreFilteredAndMovedByCategory()
	{
		( string code, _ ) = await SeedAustraliaAsync();
		await InsertAsync( NewLocalityRecord( code, "NSW", "2010", "Surry Hills", category: "SUBURB" ) );
		await InsertAsync( NewLocalityRecord( code, "NSW", "2011", "Sydney Old", retired: Base.AddHours( 1 ), category: "SUBURB" ) );

		Assert.Equal( new[] { "SURRY HILLS" },
					( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, null, "SUBURB", false ) ) ).Select( l => Key( l.Definition.Name ) ) );
		Assert.Equal( 2, ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, null, "SUBURB", true ) ) ).Count() );
		Assert.Equal( new[] { "SURRY HILLS" },
					PrefixMatches( await ReadAsync( s => s.SearchLocalitiesAsync( code, "S", null, "SUBURB", 10 ) ), "S" ).Select( l => Key( l.Definition.Name ) ) );
		Assert.True( await ReadAsync( s => s.HasLocalitiesAsync( code, "SUBURB" ) ) );
		Assert.False( await ReadAsync( s => s.HasLocalitiesAsync( code, "NONE" ) ) );

		DateTime moved = Base.AddDays( 1 );
		int count = 0;
		await WriteAsync( async session => count = await session.ReplaceLocalityCategoryAsync( code, "SUBURB", "TOWN", Context( moved, "bob" ) ) );

		Locality old = ( await ReadAsync( s => s.GetLocalityAsync( new LocalityKey( code, "NSW", "2011", "SYDNEY OLD" ) ) ) )!;

		Assert.Equal( 2, count );
		Assert.False( await ReadAsync( s => s.HasLocalitiesAsync( code, "SUBURB" ) ) );
		Assert.Equal( ( "TOWN", moved, "bob" ), ( old.Definition.Category, old.LastUpdate!.Timestamp, old.LastUpdate.Actor.Identifier ) );
		Assert.NotNull( old.Retired );

		// The move re-stamps, so a later purge keeps what it moved.
		int purged = 0;
		await WriteAsync( async session => purged = await session.DeleteLocalitiesStampedBeforeAsync( code, moved ) );
		Assert.Equal( 7, purged );
		Assert.Equal( 2, ( await ReadAsync( s => s.GetLocalitiesAsync( code, null, null, null, "TOWN", true ) ) )
							.Count( l => l.Definition.Name is "Surry Hills" or "Sydney Old" ) );
	}

	[Fact]
	public async Task AnImportedRowUnderAnotherCategoryIsRecategorized()
	{
		( string code, _ ) = await SeedAustraliaAsync();
		DateTime import = Base.AddDays( 1 );

		LocalityUpsertOutcome[] outcomes = Array.Empty<LocalityUpsertOutcome>();
		await WriteAsync( async session =>
		{
			outcomes = new[]
			{
				await session.UpsertImportedLocalityAsync( NewLocalityRecord( code, "NSW", "2000", "Sydney", category: "SUBURB" ), "2000", "SYDNEY", Context( import ) ),
				await session.UpsertImportedLocalityAsync( NewLocalityRecord( code, "ACT", "2600", "Parkes" ), "2600", "PARKES", Context( import ) ),
				await session.UpsertImportedLocalityAsync( NewLocalityRecord( code, "NSW", "2001", "Sydney Retired", category: "SUBURB" ), "2001", "SYDNEY RETIRED",
															Context( import ) )
			};
			await session.InsertImportRunAsync( new ImportRun
			{
				Identifier = Guid.NewGuid().ToString( "N" ), Country = code, Importer = "csv", Added = 0, Reinstated = 1, Deleted = 0,
				Recategorized = 1, Unchanged = 1, DatasetVersion = import, Performed = Context( import )
			} );
		} );

		Assert.Equal( new[] { LocalityUpsertOutcome.Recategorized, LocalityUpsertOutcome.Unchanged, LocalityUpsertOutcome.Reinstated }, outcomes );
		Assert.Equal( "SUBURB", ( await ReadAsync( s => s.GetLocalityAsync( new LocalityKey( code, "NSW", "2000", "SYDNEY" ) ) ) )!.Definition.Category );
		Assert.Equal( "SUBURB", ( await ReadAsync( s => s.GetLocalityAsync( new LocalityKey( code, "NSW", "2001", "SYDNEY RETIRED" ) ) ) )!.Definition.Category );
		Assert.Equal( 1, Assert.Single( await ReadAsync( s => s.GetImportRunsAsync( code ) ) ).Recategorized );
	}
}
