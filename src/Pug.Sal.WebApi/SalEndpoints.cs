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
		/// All endpoints require an authenticated caller; authorization is the facades'.
		/// Each endpoint carries OpenAPI metadata (name, tag, summary, request and response types);
		/// the host surfaces it, typically in development only, by configuring
		/// <c>AddEndpointsApiExplorer()</c> with <c>AddSwaggerGen()</c>, or
		/// <c>AddOpenApi()</c> with <c>MapOpenApi()</c>.
		/// </summary>
		public static IEndpointRouteBuilder MapSalEndpoints( this IEndpointRouteBuilder routes ) =>
			routes.MapSalDirectoryEndpoints().MapSalAdministrationEndpoints();
	}
}
