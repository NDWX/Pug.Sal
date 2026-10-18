using Moq;
using Pug.Application.Security;

namespace Pug.Sal.Tests.Infrastructure;

// ISecurityManager instances with canned authorization decisions (the Pug.Scheduling pattern).
internal static class SecurityManagers
{
	internal static ISecurityManager Authorized( string userId = "test-user" ) =>
		Create( userId, ( _, _ ) => true );

	internal static ISecurityManager Denied( string userId = "test-user" ) =>
		Create( userId, ( _, _ ) => false );

	// Grants only the given operations.
	internal static ISecurityManager Granting( params string[] operations ) =>
		Create( "test-user", ( operation, _ ) => operations.Contains( operation ) );

	// Grants every operation, but only on resources of the given SecurityObjectTypes type.
	internal static ISecurityManager GrantingOn( string resourceType ) =>
		Create( "test-user", ( _, resource ) => resource.Type == resourceType );

	// No current principal: Sal refuses such calls (DECISIONS D8).
	internal static ISecurityManager WithoutPrincipal()
	{
		Mock<ISecurityManager> manager = new();
		manager.Setup( x => x.CurrentPrincipal ).Returns( (IPrincipal)null! );

		return manager.Object;
	}

	// A principal whose identity is not authenticated (what an identity accessor returns for an
	// anonymous caller), granted every operation: Sal refuses it without asking.
	internal static ISecurityManager Unauthenticated() =>
		Create( string.Empty, ( _, _ ) => true, authenticated: false );

	private static ISecurityManager Create( string userId, Func<string, NounQualifier, bool> isAuthorized, bool authenticated = true )
	{
		Mock<IPrincipalIdentity> identity = new();
		identity.Setup( x => x.Identifier ).Returns( userId );
		identity.Setup( x => x.IsAuthenticated ).Returns( authenticated );

		Mock<IPrincipal> principal = new();
		principal.Setup( x => x.Identity ).Returns( identity.Object );
		principal
			.Setup( x => x.IsAuthorizedAsync(
				It.IsAny<IDictionary<string, string>>(),
				It.IsAny<string>(), It.IsAny<NounQualifier>(),
				It.IsAny<string>() ) )
			.ReturnsAsync( ( IDictionary<string, string> _, string operation, NounQualifier resource, string _ ) =>
								isAuthorized( operation, resource ) );

		Mock<ISecurityManager> manager = new();
		manager.Setup( x => x.CurrentPrincipal ).Returns( principal.Object );

		return manager.Object;
	}
}
