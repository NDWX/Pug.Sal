using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pug.Sal.WebApi;

namespace Pug.Sal.Tests.Infrastructure;

// SalApiFixture's host, mapped with per-surface requirements (DECISIONS D23): the directory needs
// the scope "sal.read", administration the host policy "SalAdmin" (role "sal-admin"). Not seeded.
public sealed class ScopedSalApiFixture : SalApiFixture
{
	public const string DirectoryScope = "sal.read";

	public const string AdministrationPolicy = "SalAdmin";

	public const string AdministrationRole = "sal-admin";

	protected override void ConfigureServices( IServiceCollection services )
	{
		base.ConfigureServices( services );
		services.AddAuthorization( options => options.AddPolicy( AdministrationPolicy, policy => policy.RequireRole( AdministrationRole ) ) );
	}

	protected override void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
		endpoints.MapSalEndpoints( options =>
		{
			options.Directory.Scopes = new[] { DirectoryScope };
			options.Administration.Policy = AdministrationPolicy;
		} );

	protected override Task SeedAsync() => Task.CompletedTask;
}
