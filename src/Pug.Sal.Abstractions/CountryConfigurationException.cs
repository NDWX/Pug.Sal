using System;

namespace Pug.Sal.Abstractions
{
	// Thrown when a write would leave a country in a configuration Sal does not allow, such as
	// without an enabled category (DECISIONS D28). The request is well-formed; the country's
	// current state refuses it.
	public class CountryConfigurationException : Exception
	{
		public string Country { get; }

		public CountryConfigurationException( string country, string message )
			: base( message )
		{
			Country = country;
		}
	}
}
