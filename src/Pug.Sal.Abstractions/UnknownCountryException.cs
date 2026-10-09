using System;

namespace Pug.Sal.Abstractions
{
	// Thrown by operations naming a country Sal does not hold.
	public class UnknownCountryException : Exception
	{
		public string Country { get; }

		public UnknownCountryException( string country )
			: base( $"Unknown country '{country}'" )
		{
			Country = country;
		}
	}
}
