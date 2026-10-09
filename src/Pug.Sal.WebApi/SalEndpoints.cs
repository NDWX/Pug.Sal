using Microsoft.AspNetCore.Routing;

namespace Pug.Sal.WebApi
{
	public static class SalEndpoints
	{
		// Maps the whole Sal surface: the directory under sal/ and administration under sal/admin/.
		// Pass app to mount at the application root, or a group from app.MapGroup( "/prefix" ) to
		// mount beneath a further prefix. A host exposing only reads maps
		// MapSalDirectoryEndpoints() alone.
		public static IEndpointRouteBuilder MapSalEndpoints( this IEndpointRouteBuilder routes ) =>
			routes.MapSalDirectoryEndpoints().MapSalAdministrationEndpoints();
	}
}
