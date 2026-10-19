using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Pug.Application.Security;
using Pug.Sal.Abstractions;
using Pug.Sal.Client;
using Pug.Sal.DependencyInjection;
using Pug.Sal.Importers.Csv;
using Pug.Sal.Models;
using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

// The HTTP surface end to end: LocalityDirectoryClient and LocalityAdministrationClient against
// MapSalEndpoints() and the DI-composed facades.
public class SalApiTests : IClassFixture<SalApiFixture>
{
	private readonly SalApiFixture _fixture;

	private readonly LocalityDirectoryClient _directory;

	private readonly LocalityAdministrationClient _admin;

	public SalApiTests( SalApiFixture fixture )
	{
		_fixture = fixture;
		_directory = fixture.DirectoryClient( SalApiFixture.Reader );
		_admin = fixture.AdministrationClient( SalApiFixture.Admin );
	}

	// Tests that write use a country of their own, so the AU reads stay stable.
	private static int _countryCounter;

	private async Task<string> NewCountryAsync()
	{
		int n = Interlocked.Increment( ref _countryCounter );
		string code = $"Q{(char)( 'A' + n % 26 )}";

		await _admin.CreateCountryAsync( new CountryDefinition
		{
			Code = code, Name = "Country " + code, PostcodePattern = @"^\d{4}$", PostcodeRequired = true, HasSubdivisions = true,
			Categories = new[] { "TOWN" }
		} );
		await _admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = code, Code = "S1", Name = "One" } );

		return code;
	}

	private static string[] Keys( IEnumerable<LocalityInfo> localities ) =>
		localities.Select( l => $"{l.Definition.Subdivision}|{l.Definition.Postcode}|{l.Definition.Name}" ).ToArray();

	// == Directory ==

	[Fact]
	public async Task DirectoryReadsRoundTrip()
	{
		CountryInfo au = await _directory.GetCountryAsync( "au" );
		Versioned<IEnumerable<SubdivisionInfo>> subdivisions = await _directory.GetSubdivisionsAsync( "AU" );
		Versioned<SubdivisionInfo> nsw = await _directory.GetSubdivisionAsync( "AU", "NSW" );
		Versioned<IEnumerable<LocalityInfo>> shared = await _directory.GetLocalitiesAsync( "AU", postcode: "2620" );
		Versioned<LocalityInfo> kilda = await _directory.GetLocalityAsync( "AU", "VIC", "3182", "st kilda" );
		Versioned<IEnumerable<LocalityInfo>> search = await _directory.SearchLocalitiesAsync( "AU", "s" );

		Assert.Equal( ( "AU", "Australia", DateTimeKind.Utc ), ( au.Definition.Code, au.Definition.Name, au.CurrentVersion.Kind ) );
		Assert.Contains( await _directory.GetCountriesAsync(), c => c.Definition.Code == "AU" );
		Assert.Contains( await _directory.GetCountryVersionsAsync(), v => v.Country == "AU" && v.Version == au.CurrentVersion );
		Assert.Equal( new[] { "ACT", "NSW", "VIC" }, subdivisions.Value.Select( s => s.Definition.Code ) );
		Assert.Equal( au.CurrentVersion, subdivisions.DatasetVersion );
		Assert.Equal( "New South Wales", nsw.Value.Definition.Name );
		Assert.Equal( new[] { "ACT|2620|Hume", "NSW|2620|Environa" }, Keys( shared.Value ) );
		Assert.Equal( "St Kilda", kilda.Value.Definition.Name );
		Assert.Equal( new[] { "VIC|3182|St Kilda", "NSW|2000|Sydney" }, Keys( search.Value ) );
	}

	[Fact]
	public async Task ValidationRoundTrips()
	{
		ValidationResult valid = await _directory.ValidateAsync( new AddressCandidate { Country = "AU", Subdivision = "nsw", Locality = "sydney", Postcode = "2000" } );
		ValidationResult mismatch = await _directory.ValidateAsync( new AddressCandidate { Country = "AU", Subdivision = "ACT", Locality = "Environa", Postcode = "2620" } );

		Assert.True( valid.IsValid );
		Assert.Equal( new AddressCandidate { Country = "AU", Subdivision = "NSW", Locality = "Sydney", Postcode = "2000", Category = "SUBURB" },
					valid.Match );
		Assert.Equal( ValidationOutcomes.Mismatch, mismatch.Combination );
		Assert.Equal( "NSW", Assert.Single( mismatch.Suggestions ).Subdivision );
	}

	[Fact]
	public async Task FailuresSurfaceAsTheFacadesExceptions()
	{
		UnknownCountryException country = await Assert.ThrowsAsync<UnknownCountryException>( () => _directory.GetCountryAsync( "zz" ) );
		UnknownSubdivisionException subdivision = await Assert.ThrowsAsync<UnknownSubdivisionException>( () => _directory.GetSubdivisionAsync( "AU", "XX" ) );
		UnknownLocalityException locality = await Assert.ThrowsAsync<UnknownLocalityException>(
			() => _directory.GetLocalityAsync( "AU", "VIC", "2000", "Sydney" ) );
		ArgumentException text = await Assert.ThrowsAsync<ArgumentException>( () => _directory.SearchLocalitiesAsync( "AU", "" ) );
		ArgumentException limit = await Assert.ThrowsAsync<ArgumentException>( () => _directory.SearchLocalitiesAsync( "AU", "s", limit: 0 ) );

		Assert.Equal( "ZZ", country.Country );
		Assert.Equal( ( "AU", "XX" ), ( subdivision.Country, subdivision.Subdivision ) );
		Assert.Equal( ( "AU", "VIC", "2000", "Sydney" ), ( locality.Country, locality.Subdivision, locality.Postcode, locality.Name ) );
		Assert.Equal( "text", text.ParamName );
		Assert.Equal( "limit", limit.ParamName );
	}

	[Fact]
	public async Task AuthenticationAndAuthorization()
	{
		using HttpResponseMessage anonymous = await _fixture.CreateClient( null ).GetAsync( "directory/countries" );

		Assert.Equal( HttpStatusCode.Unauthorized, anonymous.StatusCode );
		await Assert.ThrowsAsync<AuthenticationException>( () => _fixture.DirectoryClient( null ).GetCountriesAsync() );
		await Assert.ThrowsAsync<AccessDeniedException>( () => _fixture.DirectoryClient( "nobody" ).GetCountriesAsync() );
		await Assert.ThrowsAsync<AccessDeniedException>( () => _fixture.AdministrationClient( SalApiFixture.Reader ).RetireCountryAsync( "AU" ) );
		Assert.Equal( "seed", Assert.Single( await _fixture.AdministrationClient( SalApiFixture.Admin ).GetImportRunsAsync( "AU" ) ).Source );
	}

	// A created record's Location is the path of its read endpoint, under the surface's prefix.
	[Fact]
	public async Task CreatedRecordsLocateTheirReadEndpoints()
	{
		string code = await NewCountryAsync();
		HttpClient admin = _fixture.CreateClient( SalApiFixture.Admin );

		using HttpResponseMessage country = await admin.PostAsJsonAsync( "administration/countries",
										new CountryDefinition { Code = "XL", Name = "Located", HasSubdivisions = true, Categories = new[] { "TOWN" } } );
		using HttpResponseMessage subdivision = await admin.PostAsJsonAsync( $"administration/countries/{code}/subdivisions",
										new SubdivisionDefinition { Code = "S 2", Name = "Two" } );
		using HttpResponseMessage locality = await admin.PostAsJsonAsync( $"administration/countries/{code}/localities",
										new LocalityDefinition { Subdivision = "S1", Postcode = "2000", Name = "Mount Town", Category = "TOWN" } );

		Assert.Equal( "/administration/countries/XL", country.Headers.Location!.OriginalString );
		Assert.Equal( $"/administration/countries/{code}/subdivisions/S%202", subdivision.Headers.Location!.OriginalString );
		Assert.Equal( $"/administration/countries/{code}/locality?subdivision=S1&postcode=2000&name=Mount%20Town",
					locality.Headers.Location!.OriginalString );

		foreach( HttpResponseMessage created in new[] { country, subdivision, locality } )
		{
			Assert.Equal( HttpStatusCode.Created, created.StatusCode );

			using HttpResponseMessage read = await admin.GetAsync( created.Headers.Location );
			Assert.Equal( HttpStatusCode.OK, read.StatusCode );
		}
	}

	// Only AccessDeniedException is a refusal (403). An operating-system UnauthorizedAccessException
	// is a server fault: it is not mapped, so the caller never sees it as a refusal (D25).
	[Fact]
	public async Task AnOperatingSystemAccessFailureIsNotARefusal()
	{
		HttpStatusCode? status = null;

		Exception? thrown = await Record.ExceptionAsync( async () =>
		{
			using HttpResponseMessage response = await _fixture.CreateClient( SalApiFixture.Admin ).PostAsync(
				"administration/countries/AU/imports?importer=os-denied",
				new ByteArrayContent( new byte[] { 1 } ) { Headers = { ContentType = new( "application/octet-stream" ) } } );

			status = response.StatusCode;
		} );

		Assert.NotEqual( HttpStatusCode.Forbidden, status );
		Assert.True( status == HttpStatusCode.InternalServerError || thrown is UnauthorizedAccessException and not AccessDeniedException,
					$"status {status}, thrown {thrown?.GetType().Name}" );
	}

	[Fact]
	public async Task ReadsAnswerIfNoneMatchUntilTheCountryChanges()
	{
		string code = await NewCountryAsync();
		HttpClient client = _fixture.CreateClient( SalApiFixture.Reader );
		string path = $"directory/countries/{code}/subdivisions";

		using HttpResponseMessage first = await client.GetAsync( path );
		EntityTagHeaderValue tag = first.Headers.ETag!;

		using HttpRequestMessage again = new( HttpMethod.Get, path );
		again.Headers.IfNoneMatch.Add( tag );
		using HttpResponseMessage unchanged = await client.SendAsync( again );

		await _admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = code, Code = "S2", Name = "Two" } );

		using HttpRequestMessage after = new( HttpMethod.Get, path );
		after.Headers.IfNoneMatch.Add( tag );
		using HttpResponseMessage changed = await client.SendAsync( after );

		Assert.Equal( HttpStatusCode.OK, first.StatusCode );
		Assert.Equal( HttpStatusCode.NotModified, unchanged.StatusCode );
		Assert.Equal( HttpStatusCode.OK, changed.StatusCode );
		Assert.NotEqual( tag, changed.Headers.ETag );
	}

	// == Administration ==

	[Fact]
	public async Task AdministrationWritesRoundTripWithAuditContexts()
	{
		string code = await NewCountryAsync();

		Versioned<Locality> created = await _admin.CreateLocalityAsync( new LocalityDefinition { Country = code, Subdivision = "S1", Postcode = "1000", Name = "Alpha", Category = "TOWN" } );
		Versioned<Locality> retired = await _admin.RetireLocalityAsync( code, "S1", "1000", "alpha" );
		Versioned<Locality> reinstated = await _admin.ReinstateLocalityAsync( code, "S1", "1000", "Alpha" );
		Country updated = await _admin.UpdateCountryAsync( ( await _admin.GetCountryAsync( code ) ).Definition with { Name = "Renamed" } );
		Versioned<Subdivision> subdivision = await _admin.UpdateSubdivisionAsync( new SubdivisionDefinition { Country = code, Code = "S1", Name = "Uno" } );
		DateTime deleted = await _admin.DeleteLocalityAsync( code, "S1", "1000", "Alpha" );

		Assert.Equal( SalApiFixture.Admin, created.Value.Registration.Actor.Identifier );
		Assert.Equal( created.DatasetVersion, created.Value.Registration.Timestamp );
		Assert.Equal( retired.DatasetVersion, retired.Value.Retired );
		Assert.Null( reinstated.Value.Retired );
		Assert.Equal( "Renamed", updated.Definition.Name );
		Assert.Equal( "Uno", subdivision.Value.Definition.Name );
		Assert.Equal( deleted, ( await _admin.GetCountryAsync( code ) ).CurrentVersion );
		Assert.Equal( DateTimeKind.Utc, deleted.Kind );
		Assert.Contains( _fixture.EventPublisher.Changes, c => c.Country == code && c.Kind == ChangeKinds.Deleted && c.DatasetVersion == deleted );
		await Assert.ThrowsAsync<UnknownLocalityException>( () => _admin.GetLocalityAsync( code, "S1", "1000", "Alpha" ) );
	}

	// == Categories (DECISIONS D28) ==

	[Fact]
	public async Task CategoriesRoundTrip()
	{
		Versioned<IEnumerable<CountryCategoryInfo>> au = await _directory.GetCountryCategoriesAsync( "AU" );
		Versioned<IEnumerable<LocalityInfo>> towns = await _directory.GetLocalitiesAsync( "AU", category: "TOWN" );
		Versioned<IEnumerable<LocalityInfo>> suburbs = await _directory.SearchLocalitiesAsync( "AU", "s", category: "SUBURB" );

		Assert.Equal( new[] { "SUBURB", "TOWN" }, ( await _directory.GetCategoriesAsync() ).Select( c => c.Definition.Code ).Take( 2 ) );
		Assert.Equal( new[] { "SUBURB|Suburb", "TOWN|Town" }, au.Value.Select( c => $"{c.Category}|{c.Name}" ) );
		Assert.Equal( new[] { "NSW|2620|Environa" }, Keys( towns.Value ) );
		Assert.Equal( "SUBURB", ( await _directory.GetLocalityAsync( "AU", "ACT", "2620", "Hume" ) ).Value.Definition.Category );
		Assert.Equal( new[] { "VIC|3182|St Kilda", "NSW|2000|Sydney" }, Keys( suburbs.Value ) );

		string code = await NewCountryAsync();

		Category village = await _admin.CreateCategoryAsync( new CategoryDefinition { Code = "VILLAGE", Name = "Villag" } );
		Category renamed = await _admin.UpdateCategoryAsync( new CategoryDefinition { Code = "VILLAGE", Name = "Village" } );
		Versioned<CountryCategory> assigned = await _admin.AssignCategoryAsync( code, "VILLAGE" );
		CountryCategory listed = Assert.Single( await _admin.GetCategoryCountriesAsync( "VILLAGE" ) );
		await _admin.CreateLocalityAsync( new LocalityDefinition { Country = code, Subdivision = "S1", Postcode = "1000", Name = "Alpha", Category = "VILLAGE" } );
		Versioned<CountryCategory> disabled = await _admin.DisableCategoryAsync( code, "VILLAGE", "TOWN" );
		Versioned<CountryCategory> enabled = await _admin.EnableCategoryAsync( code, "VILLAGE" );
		DateTime unassigned = await _admin.UnassignCategoryAsync( code, "VILLAGE" );
		Category retired = await _admin.RetireCategoryAsync( "VILLAGE" );

		Assert.Equal( SalApiFixture.Admin, village.Registration.Actor.Identifier );
		Assert.Equal( "Village", renamed.Definition.Name );
		Assert.Equal( ( "VILLAGE", "Village" ), ( assigned.Value.Category, assigned.Value.Name ) );
		Assert.Equal( ( code, (DateTime?)null, SalApiFixture.Admin ), ( listed.Country, listed.Disabled, listed.Registration.Actor.Identifier ) );
		Assert.Equal( disabled.DatasetVersion, disabled.Value.Disabled );
		Assert.Null( enabled.Value.Disabled );
		Assert.Equal( "TOWN", ( await _admin.GetLocalityAsync( code, "S1", "1000", "Alpha" ) ).Value.Definition.Category );
		Assert.Equal( unassigned, ( await _admin.GetCountryAsync( code ) ).CurrentVersion );
		Assert.Equal( new[] { "TOWN" }, ( await _admin.GetCountryCategoriesAsync( code, includeDisabled: false ) ).Value.Select( c => c.Category ) );
		Assert.NotNull( ( await _admin.GetCategoryAsync( "VILLAGE" ) ).Retired );
		Assert.NotNull( retired.Retired );
		Assert.Null( ( await _admin.ReinstateCategoryAsync( "VILLAGE" ) ).Retired );
		Assert.Contains( _fixture.EventPublisher.Replacements,
						r => r.Country == code && ( r.Category, r.Replacement, r.Localities ) == ( "VILLAGE", "TOWN", 1 ) );

		UnknownCategoryException global = await Assert.ThrowsAsync<UnknownCategoryException>( () => _admin.GetCategoryAsync( "NOPE" ) );
		UnknownCategoryException unassignedCategory = await Assert.ThrowsAsync<UnknownCategoryException>(
			() => _admin.CreateLocalityAsync( new LocalityDefinition { Country = code, Subdivision = "S1", Postcode = "1001", Name = "Beta", Category = "SUBURB" } ) );
		CountryConfigurationException last = await Assert.ThrowsAsync<CountryConfigurationException>( () => _admin.DisableCategoryAsync( code, "TOWN" ) );

		Assert.Equal( ( (string?)null, "NOPE" ), ( global.Country, global.Category ) );
		Assert.Equal( ( code, "SUBURB" ), ( unassignedCategory.Country, unassignedCategory.Category ) );
		Assert.Equal( code, last.Country );
		Assert.Contains( "at least one enabled category", last.Message );
	}

	[Fact]
	public async Task AdministrationFailuresSurfaceAsTheFacadesExceptions()
	{
		string code = await NewCountryAsync();

		DuplicateEntryException duplicate = await Assert.ThrowsAsync<DuplicateEntryException>(
			() => _admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = code, Code = "S1", Name = "Again" } ) );
		ArgumentException invalid = await Assert.ThrowsAsync<ArgumentException>(
			() => _admin.CreateCountryAsync( new CountryDefinition { Code = "AUS", Name = "Too long" } ) );

		Assert.Equal( ( ChangeEntityTypes.Subdivision, "S1" ), ( duplicate.EntityType, duplicate.Key ) );
		Assert.Equal( nameof(CountryDefinition.Code), invalid.ParamName );
		await Assert.ThrowsAsync<UnknownSubdivisionException>(
			() => _admin.CreateLocalityAsync( new LocalityDefinition { Country = code, Subdivision = "S9", Postcode = "1000", Name = "Nowhere", Category = "TOWN" } ) );
		await Assert.ThrowsAsync<UnknownCountryException>( () => _admin.RetireCountryAsync( "ZY" ) );
	}

	[Fact]
	public async Task ImportsStreamTheDatasetWithOptions()
	{
		string code = await NewCountryAsync();
		Dictionary<string, string> options = new()
		{
			[ "locality" ] = "suburb", [ "subdivision" ] = "state", [ "category" ] = "kind", [ "defaultCategory" ] = "TOWN", [ "delimiter" ] = ";"
		};

		ImportRun run = await _admin.ImportAsync( code, new ImportRequest { Importer = "CSV", Source = "upload.csv", Options = options },
			new MemoryStream( Encoding.UTF8.GetBytes( "state;postcode;suburb;kind\nS1;1000;Alpha;TOWN\nS1;1001;Beta;\n" ) ) );

		DatasetImportException invalid = await Assert.ThrowsAsync<DatasetImportException>(
			() => _admin.ImportAsync( code, new ImportRequest { Importer = "csv" },
				new MemoryStream( Encoding.UTF8.GetBytes( "subdivision,postcode,locality,category\nS1,1000,Alpha,TOWN\nS9,1001,Beta,TOWN\n" ) ) ) );
		UnknownDatasetImporterException importer = await Assert.ThrowsAsync<UnknownDatasetImporterException>(
			() => _admin.ImportAsync( code, new ImportRequest { Importer = "xml" }, new MemoryStream() ) );

		Assert.Equal( ( 2, "upload.csv", "CSV" ), ( run.Added, run.Source, run.Importer ) );
		Assert.Equal( run.DatasetVersion, ( await _admin.GetCountryAsync( code ) ).CurrentVersion );
		Assert.Equal( 2, invalid.Position );
		Assert.Equal( "Unknown subdivision 'S9' of country '" + code + "'; imports never create subdivisions. (at 2)", invalid.Message );
		Assert.Equal( "xml", importer.Importer );
		Assert.Equal( run.Identifier, Assert.Single( await _admin.GetImportRunsAsync( code ) ).Identifier );
		Assert.Contains( _fixture.EventPublisher.Imports, i => i.Identifier == run.Identifier );
	}

	// == Composition ==

	[Fact]
	public void AddSalComposesTheFacadesWithDefaults()
	{
		ServiceCollection services = new();
		services.AddSingleton( Mock.Of<Pug.Application.Data.IApplicationData<Pug.Sal.Data.Abstractions.IDataSession>>() );
		services.AddSingleton( Mock.Of<ISecurityManager>() );
		services.AddSal();
		services.AddSalDatasetImporter<CsvDatasetImporter>( "csv" );

		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.IsType<LocalityDirectory>( provider.GetRequiredService<ILocalityDirectory>() );
		Assert.IsType<LocalityAdministration>( provider.GetRequiredService<ILocalityAdministration>() );
		Assert.Same( NullLocalityChangeEventPublisher.Instance, provider.GetRequiredService<ILocalityChangeEventPublisher>() );
		Assert.IsType<CsvDatasetImporter>( provider.GetRequiredService<IDatasetImporterRegistry>().GetImporter( "CSV" ) );
		Assert.Throws<ArgumentException>( () => services.AddSalDatasetImporter<CsvDatasetImporter>( "Csv" ) );
	}
}
