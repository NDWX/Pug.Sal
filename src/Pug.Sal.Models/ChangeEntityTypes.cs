namespace Pug.Sal.Models
{
	// Well-known values of LocalityChange.EntityType.
	public static class ChangeEntityTypes
	{
		public const string Country = "COUNTRY";

		public const string Subdivision = "SUBDIVISION";

		public const string Locality = "LOCALITY";

		// A global category (LocalityChange.Country is null).
		public const string Category = "CATEGORY";

		// A category's assignment to a country: CREATED when assigned, DELETED when unassigned,
		// RETIRED when disabled, REINSTATED when enabled.
		public const string CountryCategory = "COUNTRY_CATEGORY";
	}
}
