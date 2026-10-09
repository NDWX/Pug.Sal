namespace Pug.Sal.Models
{
	// Well-known values of LocalityChange.Kind.
	public static class ChangeKinds
	{
		public const string Created = "CREATED";

		// Countries and subdivisions only: localities are never updated.
		public const string Updated = "UPDATED";

		public const string Retired = "RETIRED";

		public const string Reinstated = "REINSTATED";

		// Localities only, removed by an administrator. Deletions by an import are counted in its
		// ImportRun instead.
		public const string Deleted = "DELETED";
	}
}
