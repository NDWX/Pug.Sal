namespace Pug.Sal.Abstractions
{
	// Object types under which Sal ACLs are registered in the authorization store.
	public static class SecurityObjectTypes
	{
		// Unqualified: a grant covers every country (see ResourceQualifiers.AllCountries).
		public static string Country => "COUNTRY";
	}
}
