using System.Text.RegularExpressions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// Checks and tidies the definitions administrators write. Each returns the definition as it
	// is stored: codes trimmed (country codes upper-cased), names trimmed, blank optional values
	// null. Throws ArgumentException naming the offending member.
	internal static class DefinitionRules
	{
		private static readonly Regex Alpha2 = new( "^[A-Z]{2}$", RegexOptions.CultureInvariant );

		private static readonly Regex Alpha3 = new( "^[A-Z]{3}$", RegexOptions.CultureInvariant );

		public static string CountryCode( string? code )
		{
			string value = code?.Trim().ToUpperInvariant() ?? string.Empty;

			if( !Alpha2.IsMatch( value ) )
				throw new ArgumentException( $"'{code}' is not an ISO 3166-1 alpha-2 country code.", nameof(CountryDefinition.Code) );

			return value;
		}

		public static CountryDefinition Country( CountryDefinition? definition )
		{
			if( definition is null )
				throw new ArgumentNullException( nameof(definition) );

			string? alpha3 = Optional( definition.Alpha3 )?.ToUpperInvariant();

			if( alpha3 is not null && !Alpha3.IsMatch( alpha3 ) )
				throw new ArgumentException( $"'{definition.Alpha3}' is not an ISO 3166-1 alpha-3 country code.", nameof(definition.Alpha3) );

			string? pattern = string.IsNullOrWhiteSpace( definition.PostcodePattern ) ? null : definition.PostcodePattern;

			if( pattern is not null )
				PostcodeRules.CheckPattern( pattern );

			return definition with
			{
				Code = CountryCode( definition.Code ),
				Alpha3 = alpha3!,
				Name = Required( definition.Name, nameof(definition.Name) ),
				PostcodePattern = pattern!,
				Categories = null!
			};
		}

		public static SubdivisionDefinition Subdivision( SubdivisionDefinition? definition )
		{
			if( definition is null )
				throw new ArgumentNullException( nameof(definition) );

			return definition with
			{
				Country = CountryCode( definition.Country ),
				Code = Required( definition.Code, nameof(definition.Code) ),
				Name = Required( definition.Name, nameof(definition.Name) ),
				Type = Optional( definition.Type )!
			};
		}

		// Shape only; what depends on the country (subdivision, postcode) is checked against it.
		public static LocalityDefinition Locality( LocalityDefinition? definition )
		{
			if( definition is null )
				throw new ArgumentNullException( nameof(definition) );

			return definition with
			{
				Country = CountryCode( definition.Country ),
				Subdivision = Optional( definition.Subdivision )!,
				Postcode = Optional( definition.Postcode )!,
				Name = Required( definition.Name, nameof(definition.Name) ),
				Category = Required( definition.Category, nameof(definition.Category) )
			};
		}

		public static CategoryDefinition Category( CategoryDefinition? definition )
		{
			if( definition is null )
				throw new ArgumentNullException( nameof(definition) );

			return definition with
			{
				Code = Required( definition.Code, nameof(definition.Code) ),
				Name = Required( definition.Name, nameof(definition.Name) )
			};
		}

		// A new country's first categories: trimmed, blanks dropped, duplicates removed (first
		// kept); at least one is required (DECISIONS D28).
		public static IReadOnlyList<string> Categories( IEnumerable<string>? categories )
		{
			List<string> codes = ( categories ?? Enumerable.Empty<string>() )
								.Select( Optional )
								.Where( code => code is not null )
								.Select( code => code! )
								.Distinct( StringComparer.Ordinal )
								.ToList();

			if( codes.Count == 0 )
				throw new ArgumentException( "A country needs at least one category.", nameof(CountryDefinition.Categories) );

			return codes;
		}

		private static string Required( string? value, string member ) =>
			string.IsNullOrWhiteSpace( value ) ? throw new ArgumentException( $"{member} is required.", member ) : value.Trim();

		private static string? Optional( string? value ) =>
			string.IsNullOrWhiteSpace( value ) ? null : value.Trim();
	}
}
