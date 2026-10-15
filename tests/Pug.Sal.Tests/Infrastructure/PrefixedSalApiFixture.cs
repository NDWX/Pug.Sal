using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Pug.Sal.WebApi;

namespace Pug.Sal.Tests.Infrastructure;

// SalApiFixture's host with the surfaces mounted where the host chooses (DECISIONS D20), inside an
// enclosing group: the directory at api/sal (its prefix given with slashes), administration at
// api/admin (an empty prefix on its own group). Seeded through the clients at those prefixes.
public sealed class PrefixedSalApiFixture : SalApiFixture
{
	protected override string DirectoryPrefix => "api/sal";

	protected override string AdministrationPrefix => "api/admin";

	protected override void MapEndpoints( IEndpointRouteBuilder endpoints )
	{
		IEndpointRouteBuilder api = endpoints.MapGroup( "api" );

		api.MapSalDirectoryEndpoints( directory => directory.RoutePrefix = "/sal/" );
		api.MapGroup( "admin" ).MapSalAdministrationEndpoints( administration => administration.RoutePrefix = "" );
	}
}
