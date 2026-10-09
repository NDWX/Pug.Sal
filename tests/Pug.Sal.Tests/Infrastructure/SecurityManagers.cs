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

	// No current principal: Sal refuses such calls (DECISIONS D8).
	internal static ISecurityManager WithoutPrincipal()
	{
		Mock<ISecurityManager> manager = new();
		manager.Setup( x => x.CurrentPrincipal ).Returns( (IPrincipal)null! );

		return manager.Object;
	}

	private static ISecurityManager Create( string userId, Func<string, NounQualifier, bool> isAuthorized )
	{
		Mock<IPrincipalIdentity> identity = new();
		identity.Setup( x => x.Identifier ).Returns( userId );

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
