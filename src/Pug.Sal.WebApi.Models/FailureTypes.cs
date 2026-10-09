namespace Pug.Sal.WebApi.Models
{
	// Well-known values of FailureInfo.Type, with the status each is sent with.
	public static class FailureTypes
	{
		// 404.
		public const string UnknownCountry = "UNKNOWN_COUNTRY";

		// 404.
		public const string UnknownSubdivision = "UNKNOWN_SUBDIVISION";

		// 404.
		public const string UnknownLocality = "UNKNOWN_LOCALITY";

		// 409.
		public const string DuplicateEntry = "DUPLICATE_ENTRY";

		// 422.
		public const string UnknownImporter = "UNKNOWN_IMPORTER";

		// 422.
		public const string InvalidDataset = "INVALID_DATASET";
	}
}
