using Pug.Application.Data;
using Pug.Effable;
using Pug.Sal.Models;

namespace Pug.Sal.Data.Abstractions
{
	// Storage contract consumed only by Pug.Sal's LocalityDirectory and LocalityAdministration (and
	// the latter's ImportCoordinator).
	// Methods are storage primitives: authorization, normalization, validation, version policy,
	// de-duplicating a dataset's keys and choosing timestamps all live with the caller, which
	// composes primitives into atomic units inside one transaction (DECISIONS D15).
	//
	// Every write transaction to a country starts with AdvanceVersionAsync, which takes the
	// country's write lock, and then stamps its writes with the version it returned (D13). Methods
	// addressing a record that does not exist do nothing and return false; callers read first when
	// they must tell the caller which record was unknown.
	//
	// Records are the full Country, Subdivision and Locality, audit contexts included; the
	// directory projects them to the audit-free *Info records with ToInfo() (D16).
	//
	// Insert* methods throw DuplicateKeyException when the record's key already exists.
	//
	// All DateTimes are UTC and are returned with DateTimeKind.Utc.
	public interface IDataSession : IApplicationDataSession
	{
		// == Countries ==

		// Inserts the country with its CurrentVersion as given.
		Task InsertCountryAsync( Country country );

		Task<Country?> GetCountryAsync( string country );

		// Ordered by code.
		Task<IEnumerable<Country>> GetCountriesAsync( bool includeRetired );

		// Every country, retired included, ordered by code.
		Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync();

		// Replaces the country's definition columns (not its code) and stamps LastUpdate.
		Task<bool> UpdateCountryAsync( CountryDefinition definition, ActionContext<Reference> lastUpdate );

		// Sets or clears (retired null) the country's Retired timestamp and stamps LastUpdate.
		Task<bool> SetCountryRetiredAsync( string country, DateTime? retired, ActionContext<Reference> lastUpdate );

		// Takes the country row's write lock for the rest of the current transaction, then replaces
		// its CurrentVersion with DatasetVersions.Next( current, utcNow ) and returns the new
		// version; null when the country does not exist. The lock is taken before the current
		// version is read, so concurrent callers are serialized and each gets a distinct version.
		Task<DateTime?> AdvanceVersionAsync( string country, DateTime utcNow );

		// == Subdivisions ==

		Task InsertSubdivisionAsync( Subdivision subdivision );

		Task<Subdivision?> GetSubdivisionAsync( string country, string subdivision );

		// Ordered by code.
		Task<IEnumerable<Subdivision>> GetSubdivisionsAsync( string country, bool includeRetired );

		// Replaces the subdivision's Name and Type and stamps LastUpdate.
		Task<bool> UpdateSubdivisionAsync( SubdivisionDefinition definition, ActionContext<Reference> lastUpdate );

		Task<bool> SetSubdivisionRetiredAsync( string country, string subdivision, DateTime? retired, ActionContext<Reference> lastUpdate );

		// == Localities ==

		// Inserts the locality under the given normalized keys (which must be those of
		// locality.Definition). Its stamped time is Registration's timestamp.
		Task InsertLocalityAsync( Locality locality, string? postcodeKey, string nameKey );

		Task<Locality?> GetLocalityAsync( LocalityKey key );

		// Localities of the country, each filter applied only when given (subdivision code,
		// normalized postcode, normalized name; exact matches). Ordered by subdivision, name key,
		// postcode key.
		Task<IEnumerable<Locality>> GetLocalitiesAsync( string country, string? subdivision, string? postcodeKey, string? nameKey, bool includeRetired );

		// Current localities of the country whose name key starts with namePrefixKey (taken
		// literally: LIKE wildcards in it match only themselves), optionally within one
		// subdivision. Ordered by name key, subdivision, postcode key; at most limit rows.
		Task<IEnumerable<Locality>> SearchLocalitiesAsync( string country, string namePrefixKey, string? subdivision, int limit );

		// Sets or clears the locality's Retired timestamp, stamps LastUpdate, and moves its stamped
		// time to lastUpdate's timestamp.
		Task<bool> SetLocalityRetiredAsync( LocalityKey key, DateTime? retired, ActionContext<Reference> lastUpdate );

		Task<bool> DeleteLocalityAsync( LocalityKey key );

		// Whether the country holds any locality, retired ones included.
		Task<bool> HasLocalitiesAsync( string country );

		// == Imports ==

		// One dataset row: inserts the locality when its key is missing (Registration = context,
		// LastUpdate null, current), otherwise clears Retired and stamps LastUpdate = context. Either
		// way the row's stamped time becomes context's timestamp. The caller passes each key once
		// per import.
		Task<LocalityUpsertOutcome> UpsertImportedLocalityAsync( Locality locality, string? postcodeKey, string nameKey, ActionContext<Reference> context );

		// Deletes the country's localities whose stamped time (the latest of Registration and
		// LastUpdate) is earlier than timestamp, and returns how many were deleted.
		Task<int> DeleteLocalitiesStampedBeforeAsync( string country, DateTime timestamp );

		Task InsertImportRunAsync( ImportRun run );

		// The country's import runs, most recent (by DatasetVersion, which is also the Performed
		// timestamp) first.
		Task<IEnumerable<ImportRun>> GetImportRunsAsync( string country );
	}
}
