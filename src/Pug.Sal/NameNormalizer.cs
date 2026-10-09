using System.Globalization;
using System.Text;
using Pug.Sal.Abstractions;

namespace Pug.Sal
{
	// The default INameNormalizer (DECISIONS D4). Names: trimmed, internal whitespace collapsed to
	// one space, diacritics stripped and upper-cased invariantly ("  st  kílda " -> "ST KILDA").
	// Postcodes: trimmed, whitespace collapsed and upper-cased ("sw1a  1aa" -> "SW1A 1AA").
	public sealed class NameNormalizer : INameNormalizer
	{
		public string? Normalize( string? name )
		{
			if( name is null )
				return null;

			string decomposed = CollapseWhitespace( name ).Normalize( NormalizationForm.FormD );
			StringBuilder builder = new( decomposed.Length );

			foreach( char c in decomposed )
			{
				if( CharUnicodeInfo.GetUnicodeCategory( c ) != UnicodeCategory.NonSpacingMark )
					builder.Append( c );
			}

			return builder.ToString().Normalize( NormalizationForm.FormC ).ToUpperInvariant();
		}

		public string? NormalizePostcode( string? postcode )
		{
			if( string.IsNullOrWhiteSpace( postcode ) )
				return null;

			return CollapseWhitespace( postcode ).ToUpperInvariant();
		}

		// Splitting on null separators splits on every char.IsWhiteSpace character.
		private static string CollapseWhitespace( string value ) =>
			string.Join( ' ', value.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries ) );
	}
}
