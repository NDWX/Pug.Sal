namespace Pug.Sal.Models
{
	// Projects the full records ILocalityAdministration returns onto the audit-free records
	// ILocalityDirectory publishes.
	public static class InfoProjections
	{
		public static CountryInfo ToInfo( this Country country ) =>
			new CountryInfo
			{
				Definition = country.Definition,
				CurrentVersion = country.CurrentVersion,
				Retired = country.Retired
			};

		public static SubdivisionInfo ToInfo( this Subdivision subdivision ) =>
			new SubdivisionInfo
			{
				Definition = subdivision.Definition,
				Retired = subdivision.Retired
			};

		public static LocalityInfo ToInfo( this Locality locality ) =>
			new LocalityInfo
			{
				Definition = locality.Definition,
				Retired = locality.Retired
			};

		public static CategoryInfo ToInfo( this Category category ) =>
			new CategoryInfo
			{
				Definition = category.Definition,
				Retired = category.Retired
			};

		public static CountryCategoryInfo ToInfo( this CountryCategory category ) =>
			new CountryCategoryInfo
			{
				Country = category.Country,
				Category = category.Category,
				Name = category.Name,
				Disabled = category.Disabled
			};
	}
}
