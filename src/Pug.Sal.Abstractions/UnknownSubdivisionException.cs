using System;

namespace Pug.Sal.Abstractions
{
	// Thrown by operations naming a subdivision the country does not hold.
	public class UnknownSubdivisionException : Exception
	{
		public string Country { get; }

		public string Subdivision { get; }

		public UnknownSubdivisionException( string country, string subdivision )
			: base( $"Unknown subdivision '{subdivision}' in country '{country}'" )
		{
			Country = country;
			Subdivision = subdivision;
		}
	}
}
