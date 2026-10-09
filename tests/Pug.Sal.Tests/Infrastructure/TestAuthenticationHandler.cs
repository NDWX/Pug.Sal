using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pug.Sal.Tests.Infrastructure;

// Authenticates a request as the user named by its X-Test-User header; no header, no user.
internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	internal const string SchemeName = "Test";

	internal const string UserHeader = "X-Test-User";

	public TestAuthenticationHandler( IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder )
		: base( options, logger, encoder )
	{
	}

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if( !Request.Headers.TryGetValue( UserHeader, out Microsoft.Extensions.Primitives.StringValues user ) || string.IsNullOrEmpty( user ) )
			return Task.FromResult( AuthenticateResult.NoResult() );

		ClaimsIdentity identity = new( new[] { new Claim( ClaimTypes.Name, user.ToString() ) }, SchemeName );

		return Task.FromResult( AuthenticateResult.Success( new AuthenticationTicket( new ClaimsPrincipal( identity ), SchemeName ) ) );
	}
}
