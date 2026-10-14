using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pug.Sal.Tests.Infrastructure;

// Authenticates a request as the user named by its X-Test-User header; no header, no user. Each
// X-Test-Claim header (<type>=<value>) adds a claim, such as a scope or a role.
internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	internal const string SchemeName = "Test";

	internal const string UserHeader = "X-Test-User";

	internal const string ClaimHeader = "X-Test-Claim";

	public TestAuthenticationHandler( IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder )
		: base( options, logger, encoder )
	{
	}

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if( !Request.Headers.TryGetValue( UserHeader, out Microsoft.Extensions.Primitives.StringValues user ) || string.IsNullOrEmpty( user ) )
			return Task.FromResult( AuthenticateResult.NoResult() );

		List<Claim> claims = new() { new Claim( ClaimTypes.Name, user.ToString() ) };

		if( Request.Headers.TryGetValue( ClaimHeader, out Microsoft.Extensions.Primitives.StringValues extra ) )
			foreach( string claim in extra.Where( c => c is not null ).Select( c => c! ) )
			{
				int separator = claim.IndexOf( '=' );
				claims.Add( new Claim( claim[ ..separator ], claim[ ( separator + 1 ).. ] ) );
			}

		ClaimsIdentity identity = new( claims, SchemeName );

		return Task.FromResult( AuthenticateResult.Success( new AuthenticationTicket( new ClaimsPrincipal( identity ), SchemeName ) ) );
	}
}
