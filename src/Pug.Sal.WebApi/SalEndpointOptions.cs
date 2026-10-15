using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.WebApi
{
	/// <summary>
	/// The host's settings for each Sal surface, passed to <see cref="SalEndpoints.MapSalEndpoints"/>:
	/// where it is mounted (DECISIONS D20) and what it requires (D23).
	/// </summary>
	public sealed class SalEndpointOptions
	{
		/// <summary>The directory's settings; mounted under <c>directory/</c> unless changed.</summary>
		public SalApiOptions Directory { get; } = new( SalRoutes.DefaultDirectoryPrefix );

		/// <summary>Administration's settings; mounted under <c>administration/</c> unless changed.</summary>
		public SalApiOptions Administration { get; } = new( SalRoutes.DefaultAdministrationPrefix );
	}

	/// <summary>
	/// A surface's route prefix (DECISIONS D20) and its requirements beyond an authenticated caller
	/// (D23). Scopes and policy combine: a caller must satisfy both when both are set. A caller who
	/// fails either gets 403. The facades still authorize every operation (D8).
	/// </summary>
	public sealed class SalApiOptions
	{
		internal SalApiOptions( string routePrefix )
		{
			RoutePrefix = routePrefix;
		}

		/// <summary>
		/// Where the surface is mounted, relative to the route builder it is mapped on, e.g.
		/// <c>"directory"</c> or <c>"api/sal/admin"</c>. Leading and trailing slashes are ignored, and
		/// empty mounts it on the builder itself. Defaults to <see cref="SalRoutes.DefaultDirectoryPrefix"/>
		/// or <see cref="SalRoutes.DefaultAdministrationPrefix"/>. Clients must be given the same prefix.
		/// </summary>
		public string RoutePrefix { get; set; }

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

		internal static SalApiOptions Configure( string defaultPrefix, Action<SalApiOptions>? configure )
		{
			SalApiOptions options = new( defaultPrefix );
			configure?.Invoke( options );

			return options;
		}

		// Where a surface's endpoints are mapped: a group under its prefix, or the builder itself for an
		// empty one. Validated at mapping time.
		internal static IEndpointRouteBuilder Mount( IEndpointRouteBuilder routes, SalApiOptions options )
		{
			ArgumentNullException.ThrowIfNull( options );

			string prefix = SalRoutes.NormalizePrefix( options.RoutePrefix );

			return prefix.Length == 0 ? routes : routes.MapGroup( prefix );
		}

		// The conventions every endpoint of a surface gets: an authenticated caller (the default
		// policy), then the host's policy and scopes, if any. Validated, and the scopes copied, once
		// per surface at mapping time.
		internal static Func<RouteHandlerBuilder, RouteHandlerBuilder> Conventions( SalApiOptions authorization )
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
