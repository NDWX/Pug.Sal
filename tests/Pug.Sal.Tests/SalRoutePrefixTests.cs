using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Pug.Sal.Client;
using Pug.Sal.Models;
using Pug.Sal.Tests.Infrastructure;
using Pug.Sal.WebApi;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.Tests;

// The host chooses where each surface is mounted (DECISIONS D20); the clients and the Location of a
// created record follow it.
public class SalRoutePrefixTests : IClassFixture<PrefixedSalApiFixture>
{
	private readonly PrefixedSalApiFixture _fixture;

	public SalRoutePrefixTests( PrefixedSalApiFixture fixture )
	{
		_fixture = fixture;
	}

	[Fact]
	public async Task ClientsReachTheSurfacesWhereTheHostMountedThem()
	{
		// Seeding already went through the administration client at api/admin.
		CountryInfo au = await _fixture.DirectoryClient( SalApiFixture.Reader ).GetCountryAsync( "AU" );
		Versioned<IEnumerable<LocalityInfo>> sydney =
			await _fixture.DirectoryClient( SalApiFixture.Reader ).GetLocalitiesAsync( "AU", name: "Sydney" );

		Assert.Equal( "Australia", au.Definition.Name );
		Assert.Single( sydney.Value );
	}

	[Theory]
	[InlineData( "directory/countries" )]
	[InlineData( "administration/countries" )]
	[InlineData( "api/directory/countries" )]
	public async Task NothingIsMappedAtTheDefaultPrefixes( string path )
	{
		using HttpResponseMessage response = await _fixture.CreateClient( SalApiFixture.Admin ).GetAsync( path );

		Assert.Equal( HttpStatusCode.NotFound, response.StatusCode );
	}

	[Fact]
	public async Task ACreatedRecordsLocationIncludesEveryPrefix()
	{
		HttpClient admin = _fixture.CreateClient( SalApiFixture.Admin );

		using HttpResponseMessage created = await admin.PostAsJsonAsync( "api/admin/countries",
										new CountryDefinition { Code = "XP", Name = "Prefixed", HasSubdivisions = false, Categories = new[] { "TOWN" } } );

		Assert.Equal( HttpStatusCode.Created, created.StatusCode );
		Assert.Equal( "/api/admin/countries/XP", created.Headers.Location!.OriginalString );

		using HttpResponseMessage read = await admin.GetAsync( created.Headers.Location );

		Assert.Equal( HttpStatusCode.OK, read.StatusCode );
	}

	[Theory]
	[InlineData( "a//b" )]
	[InlineData( "/ sal" )]
	public void AnInvalidPrefixIsRefused( string prefix )
	{
		WebApplication app = WebApplication.CreateSlimBuilder().Build();

		Assert.Throws<ArgumentException>( () => app.MapSalDirectoryEndpoints( a => a.RoutePrefix = prefix ) );
		Assert.Throws<ArgumentException>( () => new LocalityDirectoryClient( _fixture.CreateApiClient( null ), prefix ) );
	}

	[Fact]
	public void ANullPrefixIsRefused()
	{
		WebApplication app = WebApplication.CreateSlimBuilder().Build();

		Assert.Throws<ArgumentNullException>( () => app.MapSalAdministrationEndpoints( a => a.RoutePrefix = null! ) );
		Assert.Throws<ArgumentNullException>( () => new LocalityAdministrationClient( _fixture.CreateApiClient( null ), null! ) );
	}

	[Fact]
	public void TheDefaultsAreDirectoryAndAdministration()
	{
		Assert.Equal( "directory", SalRoutes.DefaultDirectoryPrefix );
		Assert.Equal( "administration", SalRoutes.DefaultAdministrationPrefix );
		Assert.Equal( "directory", new SalEndpointOptions().Directory.RoutePrefix );
		Assert.Equal( "administration", new SalEndpointOptions().Administration.RoutePrefix );
	}
}
