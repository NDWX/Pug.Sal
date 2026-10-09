using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

// The OpenAPI metadata MapSalEndpoints() attaches (DECISIONS D20): every endpoint is named, tagged
// and summarized, and declares exactly the statuses it can produce.
public class EndpointMetadataTests : IClassFixture<SalApiFixture>
{
	private readonly RouteEndpoint[] _endpoints;

	public EndpointMetadataTests( SalApiFixture fixture )
	{
		_endpoints = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
							.Where( e => e.RoutePattern.RawText!.StartsWith( "sal/", StringComparison.Ordinal ) )
							.ToArray();
	}

	// Endpoint name -> the statuses it declares.
	public static TheoryData<string, int[]> Statuses() => new()
	{
		{ "SalGetCountries", new[] { 200, 304, 403 } },
		{ "SalGetCountryVersions", new[] { 200, 304, 403 } },
		{ "SalGetCountry", new[] { 200, 304, 403, 404 } },
		{ "SalGetSubdivisions", new[] { 200, 304, 403, 404 } },
		{ "SalGetSubdivision", new[] { 200, 304, 403, 404 } },
		{ "SalSearchLocalities", new[] { 200, 304, 400, 403, 404 } },
		{ "SalGetLocalities", new[] { 200, 304, 403, 404 } },
		{ "SalGetLocality", new[] { 200, 304, 400, 403, 404 } },
		{ "SalValidate", new[] { 200, 400, 403, 404 } },
		{ "SalAdminGetCountries", new[] { 200, 304, 403 } },
		{ "SalAdminGetCountryVersions", new[] { 200, 304, 403 } },
		{ "SalAdminGetCountry", new[] { 200, 304, 403, 404 } },
		{ "SalAdminGetSubdivisions", new[] { 200, 304, 403, 404 } },
		{ "SalAdminGetSubdivision", new[] { 200, 304, 403, 404 } },
		{ "SalAdminSearchLocalities", new[] { 200, 304, 400, 403, 404 } },
		{ "SalAdminGetLocalities", new[] { 200, 304, 403, 404 } },
		{ "SalAdminGetLocality", new[] { 200, 304, 400, 403, 404 } },
		{ "SalAdminGetImportRuns", new[] { 200, 403, 404 } },
		{ "SalAdminCreateCountry", new[] { 201, 400, 403, 409 } },
		{ "SalAdminUpdateCountry", new[] { 200, 400, 403, 404 } },
		{ "SalAdminRetireCountry", new[] { 200, 403, 404 } },
		{ "SalAdminReinstateCountry", new[] { 200, 403, 404 } },
		{ "SalAdminCreateSubdivision", new[] { 201, 400, 403, 404, 409 } },
		{ "SalAdminUpdateSubdivision", new[] { 200, 400, 403, 404 } },
		{ "SalAdminRetireSubdivision", new[] { 200, 403, 404 } },
		{ "SalAdminReinstateSubdivision", new[] { 200, 403, 404 } },
		{ "SalAdminCreateLocality", new[] { 201, 400, 403, 404, 409 } },
		{ "SalAdminRetireLocality", new[] { 200, 400, 403, 404 } },
		{ "SalAdminReinstateLocality", new[] { 200, 400, 403, 404 } },
		{ "SalAdminDeleteLocality", new[] { 200, 400, 403, 404 } },
		{ "SalAdminImport", new[] { 200, 400, 403, 404, 422 } }
	};

	private RouteEndpoint Named( string name ) =>
		Assert.Single( _endpoints, e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name );

	[Fact]
	public void EveryEndpointIsListedNamedTaggedAndSummarized()
	{
		Assert.Equal( Statuses().Count, _endpoints.Length );

		Assert.All( _endpoints, endpoint =>
		{
			Assert.NotNull( endpoint.Metadata.GetMetadata<IEndpointNameMetadata>() );
			Assert.Contains( endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>().SelectMany( t => t.Tags ),
							tag => tag is "Sal" or "Sal administration" );
			Assert.False( string.IsNullOrWhiteSpace( endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary ) );
		} );
	}

	[Theory]
	[MemberData( nameof(Statuses) )]
	public void EachEndpointDeclaresExactlyTheStatusesItProduces( string name, int[] statuses )
	{
		int[] declared = Named( name ).Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
										.Select( p => p.StatusCode ).Distinct().OrderBy( s => s ).ToArray();

		Assert.Equal( statuses, declared );
	}

	[Fact]
	public void TheImportDocumentsItsBodyAndOpenEndedOptions()
	{
		RouteEndpoint import = Named( "SalAdminImport" );
		string description = import.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description ?? string.Empty;

		Assert.Contains( "option.<name>=<value>", description );
		Assert.Contains( "importer (required)", description );
		Assert.Contains( "text/csv", import.Metadata.GetOrderedMetadata<IAcceptsMetadata>().SelectMany( a => a.ContentTypes ) );
	}
}
