using System;

namespace Pug.Sal.Abstractions
{
	// Thrown by operations naming a global category that does not exist (Country null), or a
	// category the country has not been assigned.
	public class UnknownCategoryException : Exception
	{
		// Null when the global category itself is unknown.
		public string Country { get; }

		public string Category { get; }

		public UnknownCategoryException( string country, string category )
			: base( country is null
						? $"Unknown category '{category}'"
						: $"Category '{category}' is not assigned to country '{country}'" )
		{
			Country = country;
			Category = category;
		}
	}
}
