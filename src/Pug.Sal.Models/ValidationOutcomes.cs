namespace Pug.Sal.Models
{
	// Well-known values of ComponentValidation.Outcome and ValidationResult.Combination.
	public static class ValidationOutcomes
	{
		// The component matched a current record (or, for Combination, the components belong
		// together).
		public const string Valid = "VALID";

		// The component matched a retired record.
		public const string Retired = "RETIRED";

		// Sal holds no record matching the component.
		public const string Unknown = "UNKNOWN";

		// The component is required by the country but was not given.
		public const string Missing = "MISSING";

		// The component does not have the country's shape (e.g. fails its PostcodePattern).
		public const string Malformed = "MALFORMED";

		// The country does not use the component (e.g. no subdivisions); a given value is ignored.
		public const string NotApplicable = "NOT_APPLICABLE";

		// Not evaluated because a component it depends on already failed.
		public const string NotChecked = "NOT_CHECKED";

		// Combination only: every component exists, but they do not belong together.
		public const string Mismatch = "MISMATCH";
	}
}
