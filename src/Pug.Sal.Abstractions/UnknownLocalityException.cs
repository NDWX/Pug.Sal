using System;

namespace Pug.Sal.Abstractions
{
	// Thrown by operations naming a locality the country does not hold.
	public class UnknownLocalityException : Exception
	{
		public string Country { get; }

		// Null in countries without subdivisions.
		public string Subdivision { get; }

		// Null when the locality has no postcode.
		public string Postcode { get; }

		public string Name { get; }

		public UnknownLocalityException( string country, string subdivision, string postcode, string name )
			: base( $"Unknown locality '{name}'{( postcode == null ? "" : $" {postcode}" )}" +
					$"{( subdivision == null ? "" : $" in subdivision '{subdivision}'" )} of country '{country}'" )
		{
			Country = country;
			Subdivision = subdivision;
			Postcode = postcode;
			Name = name;
		}
	}
}
