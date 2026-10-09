namespace Pug.Sal.Abstractions
{
	// Operations checked against SecurityObjectTypes.Country.
	public static class SecurityOperations
	{
		// Every ILocalityDirectory operation, and the reads of ILocalityAdministration.
		public static string View => "VIEW";

		public static string Create => "CREATE";

		// Countries and subdivisions; localities are never updated.
		public static string Modify => "MODIFY";

		// Retiring and reinstating.
		public static string Retire => "RETIRE";

		// Deleting localities; countries and subdivisions are never deleted.
		public static string Delete => "DELETE";

		// Bulk imports and their history.
		public static string Import => "IMPORT";
	}
}
