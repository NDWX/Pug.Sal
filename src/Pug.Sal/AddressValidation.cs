using Pug.Sal.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// What validating one candidate needs from the store, read in one session: the country, all its
	// subdivisions and categories, and its localities (retired included) matching the candidate's
	// name and postcode.
	internal sealed record AddressEvidence( Country Country,
											IReadOnlyList<Subdivision> Subdivisions,
											IReadOnlyList<CountryCategory> Categories,
											IReadOnlyList<Locality> ByName,
											IReadOnlyList<Locality> ByPostcode );

	// Validates an AddressCandidate against its evidence (DECISIONS D3): each component on its own,
	// then their combination. Never corrects anything: Match and Suggestions are for the caller.
	//
	// - Country: VALID or RETIRED (an unknown country is refused before this point).
	// - Subdivision: NOT_APPLICABLE when the country has none (a given value is ignored); MISSING
	//   when blank; otherwise matched by code, by the code's part after "<country>-" (so "nsw"
	//   matches AU-NSW) or by normalized name, in that order, each case-insensitively.
	// - Postcode: MISSING when blank and required, NOT_APPLICABLE when blank and optional,
	//   MALFORMED when its normalized form does not fully match the country's PostcodePattern,
	//   otherwise matched against the postcodes the country's localities hold.
	// - Locality: MISSING when blank, otherwise matched by normalized name.
	// - Category (optional, D28): NOT_APPLICABLE when blank; otherwise matched among the country's
	//   categories by code, then by normalized name, case-insensitively: VALID when enabled, RETIRED
	//   when disabled, UNKNOWN when the country has no such category.
	// - Combination: NOT_CHECKED unless every component above is VALID, RETIRED or NOT_APPLICABLE;
	//   then VALID when a current locality has the name under the matched subdivision, with the
	//   given postcode and in the matched category (each omitted when not applicable), RETIRED when
	//   only retired ones do, and MISMATCH otherwise.
	//
	// A component matching both current and retired records reports the current one.
	internal static class AddressValidation
	{
		public const int MaximumSuggestions = 10;

		public static ValidationResult Validate( AddressCandidate candidate, AddressEvidence evidence, INameNormalizer normalizer )
		{
			CountryDefinition country = evidence.Country.Definition;
			string? nameKey = NullIfEmpty( normalizer.Normalize( candidate.Locality ) );
			string? postcodeKey = normalizer.NormalizePostcode( candidate.Postcode );

			ComponentValidation countryResult = Matched( evidence.Country.Retired, country.Code );

			Subdivision? subdivision = null;
			ComponentValidation subdivisionResult;

			if( !country.HasSubdivisions )
				subdivisionResult = Outcome( ValidationOutcomes.NotApplicable );
			else if( string.IsNullOrWhiteSpace( candidate.Subdivision ) )
				subdivisionResult = Outcome( ValidationOutcomes.Missing );
			else
			{
				subdivision = MatchSubdivision( candidate.Subdivision, country.Code, evidence.Subdivisions, normalizer );

				subdivisionResult = subdivision is null
										? Outcome( ValidationOutcomes.Unknown )
										: Matched( subdivision.Retired, subdivision.Definition.Code );
			}

			ComponentValidation postcodeResult;

			if( postcodeKey is null )
				postcodeResult = Outcome( country.PostcodeRequired ? ValidationOutcomes.Missing : ValidationOutcomes.NotApplicable );
			else if( !PostcodeRules.IsWellFormed( postcodeKey, country.PostcodePattern ) )
				postcodeResult = Outcome( ValidationOutcomes.Malformed );
			else
				postcodeResult = Matched( Best( evidence.ByPostcode ), locality => locality.Definition.Postcode );

			ComponentValidation localityResult = nameKey is null
													? Outcome( ValidationOutcomes.Missing )
													: Matched( Best( evidence.ByName ), locality => locality.Definition.Name );

			CountryCategory? category = null;
			ComponentValidation categoryResult;

			if( string.IsNullOrWhiteSpace( candidate.Category ) )
				categoryResult = Outcome( ValidationOutcomes.NotApplicable );
			else
			{
				category = MatchCategory( candidate.Category, evidence.Categories, normalizer );

				categoryResult = category is null
									? Outcome( ValidationOutcomes.Unknown )
									: Matched( category.Disabled, category.Category );
			}

			string combination;
			Locality? match = null;

			if( !IsCheckable( subdivisionResult ) || !IsCheckable( postcodeResult ) || !IsCheckable( localityResult ) ||
				!IsCheckable( categoryResult ) )
			{
				combination = ValidationOutcomes.NotChecked;
			}
			else
			{
				match = Best( evidence.ByName.Where( locality => IsUnder( locality, subdivision ) &&
																( postcodeKey is null || HasPostcode( locality, postcodeKey, normalizer ) ) &&
																( category is null || locality.Definition.Category == category.Category ) ) );

				combination = match is null ? ValidationOutcomes.Mismatch :
							match.Retired is null ? ValidationOutcomes.Valid : ValidationOutcomes.Retired;
			}

			bool isValid = countryResult.Outcome == ValidationOutcomes.Valid &&
							IsAcceptable( subdivisionResult ) &&
							IsAcceptable( postcodeResult ) &&
							localityResult.Outcome == ValidationOutcomes.Valid &&
							IsAcceptable( categoryResult ) &&
							combination == ValidationOutcomes.Valid;

			return new ValidationResult
			{
				IsValid = isValid,
				Country = countryResult,
				Subdivision = subdivisionResult,
				Locality = localityResult,
				Postcode = postcodeResult,
				Category = categoryResult,
				Combination = combination,
				Match = isValid ? ToCandidate( match!, country.Code, postcodeKey is not null ) : null!,
				Suggestions = isValid || evidence.Country.Retired is not null
								? Array.Empty<AddressCandidate>()
								: Suggest( evidence, subdivision, nameKey, postcodeKey, normalizer ),
				DatasetVersion = evidence.Country.CurrentVersion
			};
		}

		// Current localities, under current subdivisions, close to the candidate, best first:
		// 1. the name with the given postcode (in any subdivision), then the name under the matched
		//    subdivision (its actual postcodes);
		// 2. otherwise the name anywhere in the country;
		// 3. otherwise the localities holding the given postcode, under the matched subdivision when
		//    any is, else anywhere.
		private static IEnumerable<AddressCandidate> Suggest( AddressEvidence evidence, Subdivision? subdivision,
															string? nameKey, string? postcodeKey, INameNormalizer normalizer )
		{
			IReadOnlyList<Locality> usableByName = Usable( evidence.ByName, evidence.Subdivisions );
			IReadOnlyList<Locality> usableByPostcode = Usable( evidence.ByPostcode, evidence.Subdivisions );

			IEnumerable<IEnumerable<Locality>> tiers = new[]
			{
				nameKey is null
					? Enumerable.Empty<Locality>()
					: usableByName.Where( locality => postcodeKey is not null && HasPostcode( locality, postcodeKey, normalizer ) )
								.Concat( usableByName.Where( locality => subdivision is not null && IsUnder( locality, subdivision ) ) ),
				nameKey is null ? Enumerable.Empty<Locality>() : usableByName,
				usableByPostcode.Where( locality => subdivision is not null && IsUnder( locality, subdivision ) ),
				usableByPostcode
			};

			string countryCode = evidence.Country.Definition.Code;

			foreach( IEnumerable<Locality> tier in tiers )
			{
				AddressCandidate[] suggestions = tier.Distinct()
													.Take( MaximumSuggestions )
													.Select( locality => ToCandidate( locality, countryCode, true ) )
													.ToArray();

				if( suggestions.Length > 0 )
					return suggestions;
			}

			return Array.Empty<AddressCandidate>();
		}

		private static IReadOnlyList<Locality> Usable( IEnumerable<Locality> localities, IReadOnlyList<Subdivision> subdivisions ) =>
			localities.Where( locality => locality.Retired is null &&
										( locality.Definition.Subdivision is null ||
										subdivisions.Any( s => s.Definition.Code == locality.Definition.Subdivision && s.Retired is null ) ) )
					.ToList();

		private static Subdivision? MatchSubdivision( string given, string countryCode, IReadOnlyList<Subdivision> subdivisions,
													INameNormalizer normalizer )
		{
			string value = given.Trim();
			string prefix = countryCode + "-";
			string? nameKey = normalizer.Normalize( value );

			Func<Subdivision, bool>[] tiers =
			{
				s => string.Equals( s.Definition.Code, value, StringComparison.OrdinalIgnoreCase ),
				s => s.Definition.Code.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) &&
					string.Equals( s.Definition.Code.Substring( prefix.Length ), value, StringComparison.OrdinalIgnoreCase ),
				s => normalizer.Normalize( s.Definition.Name ) == nameKey
			};

			foreach( Func<Subdivision, bool> tier in tiers )
			{
				Subdivision? found = subdivisions.Where( tier ).OrderBy( s => s.Retired is null ? 0 : 1 ).FirstOrDefault();

				if( found is not null )
					return found;
			}

			return null;
		}

		private static CountryCategory? MatchCategory( string given, IReadOnlyList<CountryCategory> categories, INameNormalizer normalizer )
		{
			string value = given.Trim();
			string? nameKey = normalizer.Normalize( value );

			Func<CountryCategory, bool>[] tiers =
			{
				c => string.Equals( c.Category, value, StringComparison.OrdinalIgnoreCase ),
				c => normalizer.Normalize( c.Name ) == nameKey
			};

			foreach( Func<CountryCategory, bool> tier in tiers )
			{
				CountryCategory? found = categories.Where( tier ).OrderBy( c => c.Disabled is null ? 0 : 1 ).FirstOrDefault();

				if( found is not null )
					return found;
			}

			return null;
		}

		private static bool IsUnder( Locality locality, Subdivision? subdivision ) =>
			subdivision is null || locality.Definition.Subdivision == subdivision.Definition.Code;

		private static bool HasPostcode( Locality locality, string postcodeKey, INameNormalizer normalizer ) =>
			normalizer.NormalizePostcode( locality.Definition.Postcode ) == postcodeKey;

		// The current locality first, else the first retired one; null when there is none.
		private static Locality? Best( IEnumerable<Locality> localities )
		{
			Locality? retired = null;

			foreach( Locality locality in localities )
			{
				if( locality.Retired is null )
					return locality;

				retired ??= locality;
			}

			return retired;
		}

		private static bool IsCheckable( ComponentValidation result ) =>
			result.Outcome is ValidationOutcomes.Valid or ValidationOutcomes.Retired or ValidationOutcomes.NotApplicable;

		private static bool IsAcceptable( ComponentValidation result ) =>
			result.Outcome is ValidationOutcomes.Valid or ValidationOutcomes.NotApplicable;

		private static ComponentValidation Outcome( string outcome ) => new() { Outcome = outcome, Canonical = null! };

		private static ComponentValidation Matched( DateTime? retired, string canonical ) =>
			new()
			{
				Outcome = retired is null ? ValidationOutcomes.Valid : ValidationOutcomes.Retired,
				Canonical = canonical
			};

		private static ComponentValidation Matched( Locality? best, Func<Locality, string> canonical ) =>
			best is null ? Outcome( ValidationOutcomes.Unknown ) : Matched( best.Retired, canonical( best ) );

		private static AddressCandidate ToCandidate( Locality locality, string countryCode, bool withPostcode ) =>
			new()
			{
				Country = countryCode,
				Subdivision = locality.Definition.Subdivision,
				Locality = locality.Definition.Name,
				Postcode = withPostcode ? locality.Definition.Postcode : null!,
				Category = locality.Definition.Category
			};

		private static string? NullIfEmpty( string? value ) => string.IsNullOrEmpty( value ) ? null : value;
	}
}
