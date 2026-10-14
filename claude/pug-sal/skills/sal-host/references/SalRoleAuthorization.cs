// A role-to-operation IAuthorizationProvider for a Sal host (sal-host skill). It compiles against
// Pug.Application.Security 3.1.0 and Pug.Sal.Abstractions. Roles come from the registered
// IPrincipalRoleProvider, e.g. Entra app roles through Pug.Application.Security.AzureADRoleProvider.
using Pug.Application.Security;
using Pug.Sal.Abstractions;

public sealed class SalRoleAuthorization : RoleBasedAuthorizationProvider
{
	private static readonly Dictionary<string, string[]> Grants = new()
	{
		[ "Sal.Read" ] = new[] { SecurityOperations.View },
		[ "Sal.Maintain" ] = new[] { SecurityOperations.View, SecurityOperations.Create, SecurityOperations.Modify, SecurityOperations.Retire, SecurityOperations.Delete },
		[ "Sal.Admin" ] = new[] { SecurityOperations.View, SecurityOperations.Create, SecurityOperations.Modify, SecurityOperations.Retire, SecurityOperations.Delete, SecurityOperations.Import }
	};

	public SalRoleAuthorization( IPrincipalRoleProvider roles ) : base( roles ) { }

	public override bool PrincipalIsAuthorized( IDictionary<string, string> context, IPrincipal principal, string operation, NounQualifier resource, string purpose = "" ) =>
		PrincipalIsAuthorizedAsync( context, principal, operation, resource, purpose ).GetAwaiter().GetResult();

	public override async Task<bool> PrincipalIsAuthorizedAsync( IDictionary<string, string> context, IPrincipal principal, string operation, NounQualifier resource, string purpose = "" )
	{
		if( resource.Type != SecurityObjectTypes.Country )
			return false;

		IEnumerable<string> held = await RoleProvider.GetPrincipalRolesAsync( principal.Identity.Identifier );

		return held.Any( role => Grants.TryGetValue( role, out string[]? operations ) && operations.Contains( operation ) );
	}
}
