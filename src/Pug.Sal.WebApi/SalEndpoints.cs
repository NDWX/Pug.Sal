using Microsoft.AspNetCore.Routing;

namespace Pug.Sal.WebApi
{
	public static class SalEndpoints
	{
		/// <summary>
		/// Maps the whole Sal surface: the directory under <c>sal/</c>
		/// (<see cref="SalDirectoryEndpoints.MapSalDirectoryEndpoints"/>) and administration under
		/// <c>sal/admin/</c> (<see cref="SalAdministrationEndpoints.MapSalAdministrationEndpoints"/>).
		/// Pass <c>app</c> to mount at the application root, or a group from
		/// <c>app.MapGroup( "/prefix" )</c> to mount beneath a further prefix. A host exposing only
		/// reads maps <c>MapSalDirectoryEndpoints()</c> alone.
		/// All endpoints require an authenticated caller. <paramref name="configure"/> may add, for each
		/// surface separately, OAuth scopes (any one of them required) and a host-registered
		/// authorization policy (DECISIONS D23), e.g.
		/// <c>o =&gt; { o.Directory.Scopes = new[] { "Directory.Read" }; o.Administration.Policy = "SalAdmin"; }</c>.
		/// A caller failing them gets 403. Authorization of each operation stays the facades'.
		/// Each endpoint carries OpenAPI metadata (name, tag, summary, request and response types);
		/// the host surfaces it, typically in development only, by configuring
		/// <c>AddEndpointsApiExplorer()</c> with <c>AddSwaggerGen()</c>, or
		/// <c>AddOpenApi()</c> with <c>MapOpenApi()</c>.
		/// </summary>
		/// <param name="routes">Where to map.</param>
		/// <param name="configure">Sets each surface's scopes and policy; null for none.</param>
		public static IEndpointRouteBuilder MapSalEndpoints( this IEndpointRouteBuilder routes,
															Action<SalEndpointOptions>? configure = null )
		{
			SalEndpointOptions options = new();
			configure?.Invoke( options );

			return routes.MapDirectoryEndpoints( options.Directory ).MapAdministrationEndpoints( options.Administration );
		}
	}
}
