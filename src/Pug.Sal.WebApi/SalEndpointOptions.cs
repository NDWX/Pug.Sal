using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Pug.Sal.WebApi
{
	/// <summary>
	/// The host's authorization requirements for each Sal surface (DECISIONS D23), passed to
	/// <see cref="SalEndpoints.MapSalEndpoints"/>.
	/// </summary>
	public sealed class SalEndpointOptions
	{
		/// <summary>What the <c>sal/</c> directory endpoints require.</summary>
		public SalApiAuthorization Directory { get; } = new();

		/// <summary>What the <c>sal/admin/</c> administration endpoints require.</summary>
		public SalApiAuthorization Administration { get; } = new();
	}

	/// <summary>
	/// A surface's requirements beyond an authenticated caller (DECISIONS D23). Scopes and policy
	/// combine: a caller must satisfy both when both are set. A caller who fails either gets 403. The
	/// facades still authorize every operation (D8).
	/// </summary>
	public sealed class SalApiAuthorization
	{
		/// <summary>
		/// The caller must hold at least one of these OAuth scopes. They are compared ordinally, as
		/// they appear in the access token, so for Entra the short name in <c>scp</c>, not the
		/// <c>api://…/</c> URI. They are read from the <c>scope</c> and <c>scp</c> claims and from
		/// JwtBearer's mapped scope claim, each space-separated. Empty means no scope requirement.
		/// </summary>
		public IReadOnlyCollection<string> Scopes { get; set; } = Array.Empty<string>();

		/// <summary>
		/// The name of an authorization policy the host registered. An unregistered name fails at
		/// request time. Null means no policy.
		/// </summary>
		public string? Policy { get; set; }
	}

	internal static class SalAuthorization
	{
		// The claim types a scope travels in: OAuth's (RFC 9068), Entra's, and Entra's after
		// JwtBearer's inbound claim mapping.
		private static readonly string[] ScopeClaimTypes =
			{ "scope", "scp", "http://schemas.microsoft.com/identity/claims/scope" };

		private static readonly char[] ScopeSeparator = { ' ' };

		internal static SalApiAuthorization Configure( Action<SalApiAuthorization>? configure )
		{
			SalApiAuthorization authorization = new();
			configure?.Invoke( authorization );

			return authorization;
		}

		// The conventions every endpoint of a surface gets: an authenticated caller (the default
		// policy), then the host's policy and scopes, if any. Validated, and the scopes copied, once
		// per surface at mapping time.
		internal static Func<RouteHandlerBuilder, RouteHandlerBuilder> Conventions( SalApiAuthorization authorization )
		{
			ArgumentNullException.ThrowIfNull( authorization );

			string? policy = authorization.Policy;

			if( policy is not null && string.IsNullOrWhiteSpace( policy ) )
				throw new ArgumentException( "The policy name must not be blank.", nameof(authorization) );

			string[] scopes = authorization.Scopes?.ToArray() ?? Array.Empty<string>();

			if( scopes.Any( scope => string.IsNullOrEmpty( scope ) || scope.Any( char.IsWhiteSpace ) ) )
				throw new ArgumentException( "A scope must be non-empty and must not contain whitespace.", nameof(authorization) );

			AuthorizationPolicy? scopePolicy = scopes.Length == 0
				? null
				: new AuthorizationPolicyBuilder()
					.RequireAuthenticatedUser()
					.RequireAssertion( context => HasAnyScope( context.User, scopes ) )
					.Build();

			return endpoint =>
			{
				endpoint.RequireAuthorization();

				if( policy is not null )
					endpoint.RequireAuthorization( policy );

				if( scopePolicy is not null )
					endpoint.RequireAuthorization( scopePolicy );

				return endpoint;
			};
		}

		private static bool HasAnyScope( ClaimsPrincipal user, string[] scopes ) =>
			user.Claims
				.Where( claim => ScopeClaimTypes.Contains( claim.Type, StringComparer.Ordinal ) )
				.SelectMany( claim => claim.Value.Split( ScopeSeparator, StringSplitOptions.RemoveEmptyEntries ) )
				.Any( held => scopes.Contains( held, StringComparer.Ordinal ) );
	}
}
