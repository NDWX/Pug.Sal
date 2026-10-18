using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Pug.Sal.Models;

namespace Pug.Sal.Abstractions
{
	// Administration facade: maintenance of the reference data, manual and bulk, and the reads an
	// administrator needs. Its records are the full Country, Subdivision and Locality, audit
	// contexts (Registration, LastUpdate) included; ILocalityDirectory publishes the same data
	// without them. Implemented in-process by LocalityAdministration and over HTTP by
	// Pug.Sal.Client's LocalityAdministrationClient (DECISIONS D16).
	//
	// Authorization: the reads require SecurityOperations.View, and each write requires the
	// SecurityOperations value it names, all on ResourceQualifiers.AllCountries, except the global
	// category operations, on ResourceQualifiers.AllCategories (DECISIONS D28); AccessDeniedException
	// otherwise, and a call without an authenticated principal is refused.
	//
	// The reads behave as their ILocalityDirectory counterparts: country-scoped results carry the
	// dataset version they were read at, and unless includeRetired is set, retired records are
	// omitted from listings, while single-record lookups always return them.
	//
	// Every write commits together with a bump of the country's dataset version, returns the
	// written record with that version, and is then published to the host's
	// ILocalityChangeEventPublisher, as a LocalityChange or, for an import, as its ImportRun. Countries
	// and subdivisions are only ever retired, which is reversible; localities may also be deleted,
	// by an administrator or by an import. Retiring a retired record, or reinstating a current one,
	// changes nothing: it returns the record as it is, bumps nothing and publishes nothing.
	//
	// Localities are addressed by their key: country, subdivision code (null in countries without
	// subdivisions), postcode (null when it has none) and name, postcode and name matched on their
	// normalized forms. A locality is one locality/postcode pair, and is never updated: it is
	// created, retired, reinstated or deleted, so a correction is a delete and a create. The one
	// exception is its category, which DisableCategoryAsync with a replacement, or an import,
	// changes.
	//
	// Global categories (DECISIONS D28) are created, updated, retired and reinstated, never
	// deleted; their writes bump no country's version and publish a LocalityChange with a null
	// Country. A category is assigned to a country before its localities can use it; a country
	// always has at least one enabled category.
	public interface ILocalityAdministration
	{
		// == Reads (VIEW) ==

		// Each country carries its CurrentVersion.
		Task<IEnumerable<Country>> GetCountriesAsync( bool includeRetired = false );

		// The current dataset version of every country, retired ones included.
		Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync();

		// Throws UnknownCountryException when the country does not exist.
		Task<Country> GetCountryAsync( string country );

		// Throws UnknownCountryException when the country does not exist.
		Task<Versioned<IEnumerable<Subdivision>>> GetSubdivisionsAsync( string country, bool includeRetired = false );

		// subdivision is matched by code. Throws UnknownCountryException or
		// UnknownSubdivisionException when either does not exist.
		Task<Versioned<Subdivision>> GetSubdivisionAsync( string country, string subdivision );

		// Requires VIEW on AllCategories. The global category list, ordered by code.
		Task<IEnumerable<Category>> GetCategoriesAsync( bool includeRetired = false );

		// Requires VIEW on AllCategories. Throws UnknownCategoryException (Country null).
		Task<Category> GetCategoryAsync( string category );

		// The categories assigned to the country, ordered by code. Throws UnknownCountryException.
		Task<Versioned<IEnumerable<CountryCategory>>> GetCountryCategoriesAsync( string country, bool includeDisabled = true );

		// Lists the country's localities, narrowed by any combination of subdivision code,
		// postcode, name and category code, as ILocalityDirectory.GetLocalitiesAsync. Throws
		// UnknownCountryException when the country does not exist.
		Task<Versioned<IEnumerable<Locality>>> GetLocalitiesAsync( string country, string subdivision = null, string postcode = null, string name = null, string category = null, bool includeRetired = false );

		// A locality by its key. Throws UnknownCountryException or UnknownLocalityException when
		// either does not exist.
		Task<Versioned<Locality>> GetLocalityAsync( string country, string subdivision, string postcode, string name );

		// Autocomplete over current localities, as ILocalityDirectory.SearchLocalitiesAsync. Throws
		// UnknownCountryException when the country does not exist and ArgumentException when text
		// is empty.
		Task<Versioned<IEnumerable<Locality>>> SearchLocalitiesAsync( string country, string text, string subdivision = null, string category = null, int limit = 20 );

		// == Writes ==

		// Requires CREATE. definition.Categories names the country's first categories (at least one,
		// each a current global category); they are assigned in the same transaction. Throws
		// DuplicateEntryException when the code exists (retired or not), UnknownCategoryException
		// (Country null) for an unknown category, and ArgumentException when the definition is
		// invalid, names no category or a retired one.
		Task<Country> CreateCountryAsync( CountryDefinition definition );

		// Requires MODIFY. Replaces the definition of the country named by definition.Code;
		// definition.Categories is ignored.
		// Changing HasSubdivisions is refused (ArgumentException) while the country holds
		// localities. Throws UnknownCountryException when the country does not exist.
		Task<Country> UpdateCountryAsync( CountryDefinition definition );

		// Requires RETIRE. Retiring a country leaves its subdivisions and localities as they are;
		// they are reported under a retired country. Throws UnknownCountryException.
		Task<Country> RetireCountryAsync( string country );

		// Requires RETIRE. Throws UnknownCountryException.
		Task<Country> ReinstateCountryAsync( string country );

		// Requires CREATE. Throws UnknownCountryException, DuplicateEntryException when the code
		// exists in the country, and ArgumentException when the country has no subdivisions or the
		// definition is invalid.
		Task<Versioned<Subdivision>> CreateSubdivisionAsync( SubdivisionDefinition definition );

		// Requires MODIFY. Replaces the definition of the subdivision named by (Country, Code).
		// Throws UnknownCountryException or UnknownSubdivisionException.
		Task<Versioned<Subdivision>> UpdateSubdivisionAsync( SubdivisionDefinition definition );

		// Requires RETIRE. Leaves the subdivision's localities as they are. Throws
		// UnknownCountryException or UnknownSubdivisionException.
		Task<Versioned<Subdivision>> RetireSubdivisionAsync( string country, string subdivision );

		// Requires RETIRE.
		Task<Versioned<Subdivision>> ReinstateSubdivisionAsync( string country, string subdivision );

		// Requires CREATE. Throws UnknownCountryException, UnknownSubdivisionException,
		// UnknownCategoryException when the category is not assigned to the country,
		// DuplicateEntryException when the country already holds the key, and ArgumentException when
		// the category is disabled, the postcode is missing though required, does not match the
		// country's PostcodePattern, or the definition is otherwise invalid.
		Task<Versioned<Locality>> CreateLocalityAsync( LocalityDefinition definition );

		// Requires RETIRE. Throws UnknownCountryException or UnknownLocalityException.
		Task<Versioned<Locality>> RetireLocalityAsync( string country, string subdivision, string postcode, string name );

		// Requires RETIRE.
		Task<Versioned<Locality>> ReinstateLocalityAsync( string country, string subdivision, string postcode, string name );

		// Requires DELETE. Removes the locality; validation then reports it UNKNOWN rather than
		// RETIRED. Returns the country dataset version the deletion produced. Throws
		// UnknownCountryException or UnknownLocalityException.
		Task<DateTime> DeleteLocalityAsync( string country, string subdivision, string postcode, string name );

		// Requires IMPORT. Reads source with the named importer and makes the country's localities
		// exactly what the dataset holds, in a single transaction that holds off every other write to
		// the country until it ends. A dataset always covers the whole country:
		// - subdivisions are never created, retired, reinstated or deleted by an import: a row naming
		//   an unknown subdivision, or otherwise invalid (missing or extra subdivision, missing
		//   postcode where required, postcode not matching the country's PostcodePattern), fails the
		//   whole import; a row under a retired subdivision is imported like any other, and the
		//   subdivision stays retired;
		// - the import's timestamp is the dataset version it produces, taken after the country is
		//   locked, so it is later than every earlier write's;
		// - every row's category must be assigned to the country and enabled, and a row repeating
		//   an earlier row's key must give the same category, or the whole import fails;
		// - each row is one locality, matched by its full key: created when missing (Registration
		//   at the import's timestamp), otherwise its LastUpdate is re-stamped with the import's
		//   context, its category set to the row's, and it is reinstated when retired; a row
		//   differing from the record only in the casing or spacing of its name matches it and
		//   keeps the record's display name;
		// - every locality of the country whose latest of Registration and LastUpdate is earlier
		//   than the import's timestamp is unwanted and deleted, those added by an administrator
		//   included.
		// Always bumps the dataset version once, and publishes the ImportRun (not LocalityChanges)
		// once.
		// Throws UnknownCountryException, UnknownDatasetImporterException, and DatasetImportException
		// when the dataset is unreadable, holds no rows (which would delete the whole country), or any
		// row is invalid; a failed import applies nothing, bumps nothing and publishes nothing.
		Task<ImportRun> ImportAsync( string country, ImportRequest request, Stream source );

		// Requires IMPORT. The country's import runs, most recent first. Throws
		// UnknownCountryException.
		Task<IEnumerable<ImportRun>> GetImportRunsAsync( string country );

		// == Global categories (on AllCategories) ==

		// Requires CREATE. Throws DuplicateEntryException when the code exists (retired or not) and
		// ArgumentException when the definition is invalid.
		Task<Category> CreateCategoryAsync( CategoryDefinition definition );

		// Requires MODIFY. Replaces the name of the category named by definition.Code. Throws
		// UnknownCategoryException.
		Task<Category> UpdateCategoryAsync( CategoryDefinition definition );

		// Requires RETIRE. A retired category can no longer be assigned or enabled; countries that
		// have it keep it as it is. Throws UnknownCategoryException.
		Task<Category> RetireCategoryAsync( string category );

		// Requires RETIRE. Throws UnknownCategoryException.
		Task<Category> ReinstateCategoryAsync( string category );

		// == Country categories (on AllCountries) ==

		// Requires MODIFY. Assigns a current global category to the country, enabled; assigning
		// one already assigned changes nothing. Throws UnknownCountryException,
		// UnknownCategoryException (Country null) for an unknown category, and ArgumentException for
		// a retired one.
		Task<Versioned<CountryCategory>> AssignCategoryAsync( string country, string category );

		// Requires MODIFY. Removes the assignment. Refused with ArgumentException when any locality
		// of the country has the category, retired ones included, and with
		// CountryConfigurationException when it would leave the country without an enabled category.
		// Returns the dataset version produced. Throws UnknownCountryException or
		// UnknownCategoryException.
		Task<DateTime> UnassignCategoryAsync( string country, string category );

		// Requires RETIRE. Disables the category in the country: localities can no longer be created
		// or imported under it. With a replacement (another enabled category of the country), every
		// locality of the country with the category, retired ones included, moves to it in the
		// same transaction, its LastUpdate re-stamped; without one, they keep the disabled
		// category. Refused with CountryConfigurationException when it would leave the country
		// without an enabled category, and with ArgumentException when the replacement is the
		// category itself or disabled. Disabling a disabled
		// category without a replacement changes nothing; with one, it moves the localities still
		// holding it. Throws UnknownCountryException or UnknownCategoryException.
		Task<Versioned<CountryCategory>> DisableCategoryAsync( string country, string category, string replacement = null );

		// Requires RETIRE. Refused (ArgumentException) when the global category is retired. Throws
		// UnknownCountryException or UnknownCategoryException.
		Task<Versioned<CountryCategory>> EnableCategoryAsync( string country, string category );
	}
}
