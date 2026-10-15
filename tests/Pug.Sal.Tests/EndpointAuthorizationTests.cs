using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Pug.Sal.Tests.Infrastructure;
using Pug.Sal.WebApi;

namespace Pug.Sal.Tests;

// The host's per-surface scopes and policy (DECISIONS D23), checked before a facade is reached: the
// facades would let "admin" do everything, so each 403 here is the endpoint's.
public class EndpointAuthorizationTests : IClassFixture<ScopedSalApiFixture>
{
	private const string EntraScopeClaim = "http://schemas.microsoft.com/identity/claims/scope";

	private static readonly string AdministrationRole = $"{ClaimTypes.Role}={ScopedSalApiFixture.AdministrationRole}";

	private static readonly string DirectoryScope = $"scp={ScopedSalApiFixture.DirectoryScope}";

	private readonly ScopedSalApiFixture _fixture;

	private readonly RouteEndpoint[] _endpoints;

	public EndpointAuthorizationTests( ScopedSalApiFixture fixture )
	{
		_fixture = fixture;
		_endpoints = EndpointRequests.SalEndpoints( fixture.Services );
	}

	private async Task<HttpStatusCode> StatusAsync( HttpClient client, RouteEndpoint endpoint )
	{
		using HttpRequestMessage request = EndpointRequests.For( endpoint );
		using HttpResponseMessage response = await client.SendAsync( request );

		return response.StatusCode;
	}

	private async Task AssertEveryAsync( HttpClient client, Func<RouteEndpoint, bool> which, HttpStatusCode expected )
	{
		RouteEndpoint[] endpoints = _endpoints.Where( which ).ToArray();
		Assert.NotEmpty( endpoints );

		foreach( RouteEndpoint endpoint in endpoints )
		{
			HttpStatusCode status = await StatusAsync( client, endpoint );
			Assert.True( status == expected, $"{endpoint.RoutePattern.RawText} answered {status}" );
		}
	}

	[Fact]
	public Task EveryEndpointStillRefusesAnAnonymousCaller() =>
		AssertEveryAsync( _fixture.CreateClient( null ), _ => true, HttpStatusCode.Unauthorized );

	[Fact]
	public Task EveryDirectoryEndpointRequiresTheScope() =>
		AssertEveryAsync( _fixture.CreateClient( SalApiFixture.Admin, AdministrationRole ),
			e => !EndpointRequests.IsAdministration( e ), HttpStatusCode.Forbidden );

	[Fact]
	public Task EveryAdministrationEndpointRequiresThePolicy() =>
		AssertEveryAsync( _fixture.CreateClient( SalApiFixture.Admin, DirectoryScope ),
			EndpointRequests.IsAdministration, HttpStatusCode.Forbidden );

	[Theory]
	[InlineData( "scp=sal.read", HttpStatusCode.OK )]
	[InlineData( "scope=openid sal.read profile", HttpStatusCode.OK )]
	[InlineData( EntraScopeClaim + "=sal.read", HttpStatusCode.OK )]
	[InlineData( "scp=sal.reader", HttpStatusCode.Forbidden )]
	[InlineData( "scp=SAL.READ", HttpStatusCode.Forbidden )]
	[InlineData( "roles=sal.read", HttpStatusCode.Forbidden )]
	public async Task TheDirectoryScopeIsReadFromTheScopeClaims( string claim, HttpStatusCode expected )
	{
		using HttpResponseMessage response = await _fixture.CreateClient( SalApiFixture.Reader, claim ).GetAsync( "directory/countries" );

		Assert.Equal( expected, response.StatusCode );
	}

	[Fact]
	public async Task TheDirectoryScopeAlsoAcceptsRepeatedScopeClaims()
	{
		using HttpResponseMessage response =
			await _fixture.CreateClient( SalApiFixture.Reader, "scope=openid", "scope=sal.read" ).GetAsync( "directory/countries" );

		Assert.Equal( HttpStatusCode.OK, response.StatusCode );
	}

	[Fact]
	public async Task ThePolicyOpensAdministration()
	{
		using HttpResponseMessage response =
			await _fixture.CreateClient( SalApiFixture.Admin, AdministrationRole ).GetAsync( "administration/countries" );

		Assert.Equal( HttpStatusCode.OK, response.StatusCode );
	}

	[Fact]
	public async Task TheFacadesStillAuthorizeBehindTheScope()
	{
		// "nobody" holds the scope but no Sal permission: the directory itself refuses.
		using HttpResponseMessage response = await _fixture.CreateClient( "nobody", DirectoryScope ).GetAsync( "directory/countries" );

		Assert.Equal( HttpStatusCode.Forbidden, response.StatusCode );
	}

	[Theory]
	[InlineData( "" )]
	[InlineData( "sal.read sal.write" )]
	[InlineData( "sal.read\t" )]
	public void AnInvalidScopeIsRefusedWhenMapping( string scope )
	{
		WebApplication app = WebApplication.CreateSlimBuilder().Build();

		Assert.Throws<ArgumentException>( () => app.MapSalDirectoryEndpoints( a => a.Scopes = new[] { scope } ) );
	}

	[Fact]
	public void ABlankPolicyIsRefusedWhenMapping()
	{
		WebApplication app = WebApplication.CreateSlimBuilder().Build();

		Assert.Throws<ArgumentException>( () => app.MapSalAdministrationEndpoints( a => a.Policy = " " ) );
	}
}
