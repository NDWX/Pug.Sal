using Pug.Application.Security;
using Pug.Sal.Abstractions;
using Pug.Sal.Models;
using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

public class LocalityDirectoryTests : IClassFixture<AustraliaFixture>
{
	private readonly AustraliaFixture _fixture;

	private readonly LocalityDirectory _directory;

	public LocalityDirectoryTests( AustraliaFixture fixture )
	{
		_fixture = fixture;
		_directory = fixture.CreateDirectory();
	}

	private static string[] Keys( IEnumerable<LocalityInfo> localities ) =>
		localities.Select( l => $"{l.Definition.Subdivision}|{l.Definition.Postcode}|{l.Definition.Name}" ).ToArray();

	// == Authorization ==

	public static TheoryData<string, Func<ILocalityDirectory, Task>> Operations() => new()
	{
		{ "GetCountries", d => d.GetCountriesAsync() },
		{ "GetCountryVersions", d => d.GetCountryVersionsAsync() },
		{ "GetCountry", d => d.GetCountryAsync( "AU" ) },
		{ "GetSubdivisions", d => d.GetSubdivisionsAsync( "AU" ) },
		{ "GetSubdivision", d => d.GetSubdivisionAsync( "AU", "AU-NSW" ) },
		{ "GetLocalities", d => d.GetLocalitiesAsync( "AU" ) },
		{ "GetLocality", d => d.GetLocalityAsync( "AU", "AU-NSW", "2000", "Sydney" ) },
		{ "SearchLocalities", d => d.SearchLocalitiesAsync( "AU", "Syd" ) },
		{ "Validate", d => d.ValidateAsync( new AddressCandidate { Country = "AU" } ) }
	};

	[Theory]
	[MemberData( nameof(Operations) )]
	public async Task EveryOperationRequiresView( string operation, Func<ILocalityDirectory, Task> call )
	{
		_ = operation;

		await Assert.ThrowsAsync<NotAuthorized>( () => call( _fixture.CreateDirectory( SecurityManagers.Denied() ) ) );
		await Assert.ThrowsAsync<NotAuthorized>( () => call( _fixture.CreateDirectory( SecurityManagers.WithoutPrincipal() ) ) );
		await Assert.ThrowsAsync<NotAuthorized>( () => call( _fixture.CreateDirectory( SecurityManagers.Granting(
			SecurityOperations.Create, SecurityOperations.Modify, SecurityOperations.Retire,
			SecurityOperations.Delete, SecurityOperations.Import ) ) ) );

		await call( _fixture.CreateDirectory( SecurityManagers.Granting( SecurityOperations.View ) ) );
	}

	[Fact]
	public async Task AuthorizationIsCheckedBeforeTheCountryIsLookedUp()
	{
		await Assert.ThrowsAsync<NotAuthorized>( () => _fixture.CreateDirectory( SecurityManagers.Denied() ).GetCountryAsync( "ZZ" ) );
	}

	// == Countries ==

	[Fact]
	public async Task CountriesOmitRetiredUnlessAsked()
	{
		CountryInfo[] current = ( await _directory.GetCountriesAsync() ).ToArray();
		CountryInfo[] all = ( await _directory.GetCountriesAsync( includeRetired: true ) ).ToArray();

		Assert.Equal( new[] { "AU", "NR" }, current.Select( c => c.Definition.Code ) );
		Assert.Equal( new[] { "AU", "NR", "YU" }, all.Select( c => c.Definition.Code ) );
		Assert.Equal( AustraliaFixture.AuVersion, current[ 0 ].CurrentVersion );
	}

	[Fact]
	public async Task CountryVersionsIncludeRetiredCountries()
	{
		CountryVersion[] versions = ( await _directory.GetCountryVersionsAsync() ).ToArray();

		Assert.Equal( new[] { "AU", "NR", "YU" }, versions.Select( v => v.Country ) );
		Assert.Equal( AustraliaFixture.NrVersion, versions[ 1 ].Version );
	}

	[Fact]
	public async Task CountryLookupIgnoresCaseAndReturnsRetired()
	{
		CountryInfo au = await _directory.GetCountryAsync( " au " );
		CountryInfo yu = await _directory.GetCountryAsync( "YU" );

		Assert.Equal( "Australia", au.Definition.Name );
		Assert.Equal( @"^\d{4}$", au.Definition.PostcodePattern );
		Assert.NotNull( yu.Retired );
	}

	[Fact]
	public async Task UnknownOrBlankCountry()
	{
		UnknownCountryException unknown = await Assert.ThrowsAsync<UnknownCountryException>( () => _directory.GetCountryAsync( "zz" ) );

		Assert.Equal( "ZZ", unknown.Country );
		await Assert.ThrowsAsync<ArgumentException>( () => _directory.GetCountryAsync( " " ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _directory.GetSubdivisionsAsync( "ZZ" ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _directory.GetLocalitiesAsync( "ZZ" ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _directory.SearchLocalitiesAsync( "ZZ", "a" ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _directory.ValidateAsync( new AddressCandidate { Country = "ZZ" } ) );
	}

	// == Subdivisions ==

	[Fact]
	public async Task SubdivisionsCarryTheCountryVersion()
	{
		Versioned<IEnumerable<SubdivisionInfo>> current = await _directory.GetSubdivisionsAsync( "AU" );
		Versioned<IEnumerable<SubdivisionInfo>> all = await _directory.GetSubdivisionsAsync( "AU", includeRetired: true );

		Assert.Equal( AustraliaFixture.AuVersion, current.DatasetVersion );
		Assert.Equal( new[] { "AU-ACT", "AU-NSW", "AU-QLD", "AU-VIC" }, current.Value.Select( s => s.Definition.Code ) );
		Assert.Equal( new[] { "AU-ACT", "AU-JBT", "AU-NSW", "AU-QLD", "AU-VIC" }, all.Value.Select( s => s.Definition.Code ) );
	}

	[Fact]
	public async Task SubdivisionLookup()
	{
		Versioned<SubdivisionInfo> nsw = await _directory.GetSubdivisionAsync( "AU", "AU-NSW" );
		Versioned<SubdivisionInfo> jbt = await _directory.GetSubdivisionAsync( "AU", "AU-JBT" );

		Assert.Equal( "New South Wales", nsw.Value.Definition.Name );
		Assert.Equal( AustraliaFixture.AuVersion, nsw.DatasetVersion );
		Assert.NotNull( jbt.Value.Retired );

		UnknownSubdivisionException unknown =
			await Assert.ThrowsAsync<UnknownSubdivisionException>( () => _directory.GetSubdivisionAsync( "AU", "AU-XX" ) );

		Assert.Equal( "AU-XX", unknown.Subdivision );
	}

	// == Localities ==

	[Fact]
	public async Task LocalitiesFilteredByPostcodeSpanSubdivisions()
	{
		Versioned<IEnumerable<LocalityInfo>> shared = await _directory.GetLocalitiesAsync( "AU", postcode: " 2620 " );

		Assert.Equal( new[] { "AU-ACT|2620|Hume", "AU-NSW|2620|Environa" }, Keys( shared.Value ) );
		Assert.Equal( AustraliaFixture.AuVersion, shared.DatasetVersion );
	}

	[Fact]
	public async Task LocalitiesFilteredByNormalizedName()
	{
		Versioned<IEnumerable<LocalityInfo>> kilda = await _directory.GetLocalitiesAsync( "AU", name: "st  kilda" );
		Versioned<IEnumerable<LocalityInfo>> parkes = await _directory.GetLocalitiesAsync( "AU", name: "PARKES" );
		Versioned<IEnumerable<LocalityInfo>> nswParkes = await _directory.GetLocalitiesAsync( "AU", subdivision: "AU-NSW", name: "parkes" );

		Assert.Equal( new[] { "AU-VIC|3182|St Kilda" }, Keys( kilda.Value ) );
		Assert.Equal( new[] { "AU-ACT|2600|Parkes", "AU-NSW|2870|Parkes" }, Keys( parkes.Value ) );
		Assert.Equal( new[] { "AU-NSW|2870|Parkes" }, Keys( nswParkes.Value ) );
	}

	[Fact]
	public async Task LocalitiesOmitRetiredUnlessAsked()
	{
		Versioned<IEnumerable<LocalityInfo>> current = await _directory.GetLocalitiesAsync( "AU", name: "Sydney" );
		Versioned<IEnumerable<LocalityInfo>> all = await _directory.GetLocalitiesAsync( "AU", name: "Sydney", includeRetired: true );

		Assert.Equal( new[] { "AU-NSW|2000|Sydney" }, Keys( current.Value ) );
		Assert.Equal( new[] { "AU-NSW|2000|Sydney", "AU-NSW|2001|Sydney" }, Keys( all.Value ) );
	}

	[Fact]
	public async Task WholeCountryAndUnknownFilters()
	{
		Versioned<IEnumerable<LocalityInfo>> au = await _directory.GetLocalitiesAsync( "AU" );
		Versioned<IEnumerable<LocalityInfo>> nr = await _directory.GetLocalitiesAsync( "NR" );

		Assert.Equal( 11, au.Value.Count() );
		Assert.Equal( 12, ( await _directory.GetLocalitiesAsync( "AU", includeRetired: true ) ).Value.Count() );
		Assert.Equal( new[] { "||Yaren" }, Keys( nr.Value ) );
		Assert.Equal( AustraliaFixture.NrVersion, nr.DatasetVersion );
		Assert.Empty( ( await _directory.GetLocalitiesAsync( "AU", subdivision: "AU-XX" ) ).Value );
		Assert.Empty( ( await _directory.GetLocalitiesAsync( "AU", postcode: "9999" ) ).Value );
	}

	[Fact]
	public async Task LocalityLookupByKey()
	{
		Versioned<LocalityInfo> sydney = await _directory.GetLocalityAsync( "au", "AU-NSW", "2000", " sydney " );
		Versioned<LocalityInfo> retired = await _directory.GetLocalityAsync( "AU", "AU-NSW", "2001", "Sydney" );
		Versioned<LocalityInfo> yaren = await _directory.GetLocalityAsync( "NR", null!, null!, "yaren" );

		Assert.Equal( "Sydney", sydney.Value.Definition.Name );
		Assert.Null( sydney.Value.Retired );
		Assert.Equal( AustraliaFixture.AuVersion, sydney.DatasetVersion );
		Assert.NotNull( retired.Value.Retired );
		Assert.Equal( "Yaren", yaren.Value.Definition.Name );
	}

	[Fact]
	public async Task UnknownLocality()
	{
		UnknownLocalityException unknown = await Assert.ThrowsAsync<UnknownLocalityException>(
			() => _directory.GetLocalityAsync( "AU", "AU-VIC", "2000", "Sydney" ) );

		Assert.Equal( ( "AU", "AU-VIC", "2000", "Sydney" ), ( unknown.Country, unknown.Subdivision, unknown.Postcode, unknown.Name ) );
		await Assert.ThrowsAsync<ArgumentException>( () => _directory.GetLocalityAsync( "AU", "AU-NSW", "2000", " " ) );
	}

	// == Search ==

	[Fact]
	public async Task SearchIsANormalizedPrefixMatchOverCurrentLocalities()
	{
		Versioned<IEnumerable<LocalityInfo>> s = await _directory.SearchLocalitiesAsync( "AU", " s" );
		Versioned<IEnumerable<LocalityInfo>> syd = await _directory.SearchLocalitiesAsync( "AU", "syd" );

		Assert.Equal( new[] { "AU-QLD|4343|Spring Creek", "AU-QLD|4361|Spring Creek", "AU-VIC|3182|St Kilda", "AU-NSW|2000|Sydney" },
					Keys( s.Value ) );
		Assert.Equal( new[] { "AU-NSW|2000|Sydney" }, Keys( syd.Value ) );
		Assert.Equal( AustraliaFixture.AuVersion, s.DatasetVersion );
	}

	[Fact]
	public async Task SearchNarrowsBySubdivisionAndLimit()
	{
		Versioned<IEnumerable<LocalityInfo>> act = await _directory.SearchLocalitiesAsync( "AU", "p", subdivision: "AU-ACT" );
		Versioned<IEnumerable<LocalityInfo>> one = await _directory.SearchLocalitiesAsync( "AU", "s", limit: 1 );

		Assert.Equal( new[] { "AU-ACT|2600|Parkes" }, Keys( act.Value ) );
		Assert.Single( one.Value );
	}

	[Fact]
	public async Task SearchRejectsEmptyTextAndNonPositiveLimit()
	{
		await Assert.ThrowsAsync<ArgumentException>( () => _directory.SearchLocalitiesAsync( "AU", "  " ) );
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>( () => _directory.SearchLocalitiesAsync( "AU", "s", limit: 0 ) );
	}
}
