using System.Collections.Generic;
using System.Threading.Tasks;
using Pug.Sal.Models;

namespace Pug.Sal.Abstractions
{
	// Read facade: listing, lookup, search and validation. It is the wire contract implemented
	// in-process by LocalityDirectory and over HTTP by Pug.Sal.Client's LocalityDirectoryClient
	// (DECISIONS D16), so keep it the source of truth for Sal's read capabilities.
	//
	// Its records (CountryInfo, SubdivisionInfo, LocalityInfo) never carry the audit contexts
	// (Registration, LastUpdate); ILocalityAdministration's reads return the full records.
	//
	// Authorization: every operation requires a principal holding SecurityOperations.View on
	// ResourceQualifiers.AllCountries (GetCategoriesAsync: on ResourceQualifiers.AllCategories);
	// AccessDeniedException otherwise, and a call without an authenticated principal is refused.
	//
	// Country-scoped results carry the dataset version they were read at, so a client may cache
	// them and revalidate with GetCountryVersionsAsync. Unless includeRetired is set, retired
	// records are omitted from listings; single-record lookups always return them.
	public interface ILocalityDirectory
	{
		// Each country carries its CurrentVersion.
		Task<IEnumerable<CountryInfo>> GetCountriesAsync( bool includeRetired = false );

		// The current dataset version of every country, retired ones included: the cheap read a
		// caching client revalidates with.
		Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync();

		// Throws UnknownCountryException when the country does not exist.
		Task<CountryInfo> GetCountryAsync( string country );

		// Throws UnknownCountryException when the country does not exist.
		Task<Versioned<IEnumerable<SubdivisionInfo>>> GetSubdivisionsAsync( string country, bool includeRetired = false );

		// subdivision is matched by code. Throws UnknownCountryException or
		// UnknownSubdivisionException when either does not exist.
		Task<Versioned<SubdivisionInfo>> GetSubdivisionAsync( string country, string subdivision );

		// The global category list (DECISIONS D28), ordered by code; a category is listed whether
		// or not any country has it.
		Task<IEnumerable<CategoryInfo>> GetCategoriesAsync( bool includeRetired = false );

		// The categories assigned to the country, ordered by code; disabled ones unless
		// includeDisabled is false. Throws UnknownCountryException when the country does not exist.
		Task<Versioned<IEnumerable<CountryCategoryInfo>>> GetCountryCategoriesAsync( string country, bool includeDisabled = true );

		// Lists the country's localities, narrowed by any combination of subdivision code,
		// postcode, name (matched on its normalized form, so "st kilda" finds "St Kilda") and
		// category code. With no filter, lists the whole country — how a client caches it. Throws
		// UnknownCountryException when the country does not exist; an unknown subdivision,
		// postcode or category simply matches nothing.
		Task<Versioned<IEnumerable<LocalityInfo>>> GetLocalitiesAsync( string country, string subdivision = null, string postcode = null, string name = null, string category = null, bool includeRetired = false );

		// A locality by its key: subdivision code (null in countries without subdivisions),
		// postcode (null when it has none) and name, postcode and name matched on their normalized
		// forms. Throws UnknownCountryException or UnknownLocalityException when either does not
		// exist.
		Task<Versioned<LocalityInfo>> GetLocalityAsync( string country, string subdivision, string postcode, string name );

		// Autocomplete: current localities whose normalized name starts with the normalized text,
		// optionally within one subdivision and one category, best matches first. Stores supporting similarity
		// matching (Postgres) also return close, non-prefix matches after the prefix matches.
		// limit is capped by the service. Throws UnknownCountryException when the country does not
		// exist and ArgumentException when text is empty.
		Task<Versioned<IEnumerable<LocalityInfo>>> SearchLocalitiesAsync( string country, string text, string subdivision = null, string category = null, int limit = 20 );

		// Validates each component of the candidate on its own, then their combination; never
		// corrects anything. Subdivision and category may each be given as code or name. Throws
		// UnknownCountryException when the candidate's country does not exist.
		Task<ValidationResult> ValidateAsync( AddressCandidate candidate );
	}
}
