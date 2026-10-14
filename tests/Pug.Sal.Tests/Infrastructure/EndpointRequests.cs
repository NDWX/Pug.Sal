using System.Text;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Pug.Sal.Tests.Infrastructure;

// The mapped sal/ endpoints, and a request to each that reaches authorization.
internal static class EndpointRequests
{
	public static RouteEndpoint[] SalEndpoints( IServiceProvider services ) =>
		services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
				.Where( e => e.RoutePattern.RawText!.StartsWith( "sal/", StringComparison.Ordinal ) )
				.ToArray();

	public static bool IsAdministration( RouteEndpoint endpoint ) =>
		endpoint.RoutePattern.RawText!.StartsWith( "sal/admin/", StringComparison.Ordinal );

	// A request to endpoint, with route values and query parameters filled in. It carries a body the
	// endpoint accepts, so a refusal is authorization's, not content negotiation's.
	public static HttpRequestMessage For( RouteEndpoint endpoint )
	{
		string method = Assert.Single( endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods );
		string path = endpoint.RoutePattern.RawText!.Replace( "{country}", "AU" ).Replace( "{subdivision}", "AU-NSW" ) +
					"?name=Sydney&text=s&importer=csv";

		HttpRequestMessage request = new( new HttpMethod( method ), path );

		if( endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is { } accepts )
			request.Content = new StringContent( accepts.ContentTypes.Contains( "application/json" ) ? "{}" : "locality\nA\n",
												Encoding.UTF8, accepts.ContentTypes[ 0 ] );

		return request;
	}
}
