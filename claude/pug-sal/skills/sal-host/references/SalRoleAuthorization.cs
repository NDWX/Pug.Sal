// A role-to-operation IAuthorizationProvider for a Sal host (sal-host skill). It compiles against
// Pug.Application.Security 3.1.0 and Pug.Sal.Abstractions. Grants are per resource type: COUNTRY
// for the reference data, CATEGORY for the global locality category list (DECISIONS D28). Roles come from the registered
// IPrincipalRoleProvider, e.g. Entra app roles through Pug.Application.Security.AzureADRoleProvider.
using Pug.Application.Security;
using Pug.Sal.Abstractions;

public sealed class SalRoleAuthorization : RoleBasedAuthorizationProvider
{
	private static readonly Dictionary<string, Dictionary<string, string[]>> Grants = new()
	{
		[ SecurityObjectTypes.Country ] = new()
		{
			[ "Sal.Read" ] = new[] { SecurityOperations.View },
			[ "Sal.Maintain" ] = new[] { SecurityOperations.View, SecurityOperations.Create, SecurityOperations.Modify, SecurityOperations.Retire, SecurityOperations.Delete },
			[ "Sal.Admin" ] = new[] { SecurityOperations.View, SecurityOperations.Create, SecurityOperations.Modify, SecurityOperations.Retire, SecurityOperations.Delete, SecurityOperations.Import }
		},
		[ SecurityObjectTypes.Category ] = new()
		{
			[ "Sal.Read" ] = new[] { SecurityOperations.View },
			[ "Sal.Maintain" ] = new[] { SecurityOperations.View },
			[ "Sal.Admin" ] = new[] { SecurityOperations.View, SecurityOperations.Create, SecurityOperations.Modify, SecurityOperations.Retire }
		}
	};

	public SalRoleAuthorization( IPrincipalRoleProvider roles ) : base( roles ) { }

	public override bool PrincipalIsAuthorized( IDictionary<string, string> context, IPrincipal principal, string operation, NounQualifier resource, string purpose = "" ) =>
		PrincipalIsAuthorizedAsync( context, principal, operation, resource, purpose ).GetAwaiter().GetResult();

	public override async Task<bool> PrincipalIsAuthorizedAsync( IDictionary<string, string> context, IPrincipal principal, string operation, NounQualifier resource, string purpose = "" )
	{
		// The identity accessor returns an unauthenticated principal for an anonymous caller, rather
		// than none; never grant it anything.
		if( principal?.Identity is not { IsAuthenticated: true } || string.IsNullOrEmpty( principal.Identity.Identifier ) )
			return false;

		if( !Grants.TryGetValue( resource.Type, out Dictionary<string, string[]>? grants ) )
			return false;

		IEnumerable<string> held = await RoleProvider.GetPrincipalRolesAsync( principal.Identity.Identifier );

		return held.Any( role => grants.TryGetValue( role, out string[]? operations ) && operations.Contains( operation ) );
	}
}
