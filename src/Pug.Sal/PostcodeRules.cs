using System.Text.RegularExpressions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// A country's postcode shape (PostcodeRequired, PostcodePattern), shared by validation, the
	// locality writes and imports.
	internal static class PostcodeRules
	{
		private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds( 1 );

		// Whether the normalized postcode fully matches the pattern; any postcode matches no pattern.
		public static bool IsWellFormed( string postcodeKey, string? pattern ) =>
			pattern is null ||
			Regex.IsMatch( postcodeKey, $"^(?:{pattern})$", RegexOptions.CultureInvariant, PatternTimeout );

		// What is wrong with the normalized postcode for the country; null when nothing is.
		public static string? Problem( CountryDefinition country, string? postcodeKey )
		{
			if( postcodeKey is null )
				return country.PostcodeRequired ? $"A postcode is required in country '{country.Code}'." : null;

			return IsWellFormed( postcodeKey, country.PostcodePattern )
						? null
						: $"Postcode '{postcodeKey}' does not match the postcode pattern of country '{country.Code}'.";
		}

		// Throws ArgumentException when the pattern is not a valid regular expression.
		public static void CheckPattern( string pattern )
		{
			try
			{
				_ = new Regex( pattern, RegexOptions.CultureInvariant, PatternTimeout );
			}
			catch( ArgumentException exception )
			{
				throw new ArgumentException( $"PostcodePattern is not a valid regular expression: {exception.Message}",
											nameof(CountryDefinition.PostcodePattern), exception );
			}
		}
	}
}
