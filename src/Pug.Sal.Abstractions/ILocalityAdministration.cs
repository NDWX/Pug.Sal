using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Pug.Sal.Models;

namespace Pug.Sal.Abstractions
{
	// Write facade: maintenance of the reference data, manual and bulk. Like ILocalityDirectory,
	// it is implemented both in-process and by Pug.Sal.Client.
	//
	// Authorization: each operation requires the SecurityOperations value it names on
	// ResourceQualifiers.AllCountries; NotAuthorized otherwise, and a call without a principal is
	// refused.
	//
	// Every write commits together with a bump of the country's dataset version, returns the
	// written record with that version, and is then published to the host's
	// ILocalityChangePublisher, as a LocalityChange or, for an import, as its ImportRun. Countries and subdivisions are only ever retired, which is
	// reversible; localities may also be deleted, by an administrator or by an import.
	//
	// Localities are addressed by their key: country, subdivision code (null in countries without
	// subdivisions), postcode (null when it has none) and name, postcode and name matched on their
	// normalized forms. A locality is one locality/postcode pair, and is never updated: it is
	// created, retired, reinstated or deleted, so a correction is a delete and a create.
	public interface ILocalityAdministration
	{
		// Requires CREATE. Throws DuplicateEntryException when the code exists (retired or not) and
		// ArgumentException when the definition is invalid.
		Task<CountryInfo> CreateCountryAsync( CountryDefinition definition );

		// Requires MODIFY. Replaces the definition of the country named by definition.Code.
		// Changing HasSubdivisions is refused (ArgumentException) while the country holds
		// localities. Throws UnknownCountryException when the country does not exist.
		Task<CountryInfo> UpdateCountryAsync( CountryDefinition definition );

		// Requires RETIRE. Retiring a country leaves its subdivisions and localities as they are;
		// they are reported under a retired country. Throws UnknownCountryException.
		Task<CountryInfo> RetireCountryAsync( string country );

		// Requires RETIRE. Throws UnknownCountryException.
		Task<CountryInfo> ReinstateCountryAsync( string country );

		// Requires CREATE. Throws UnknownCountryException, DuplicateEntryException when the code
		// exists in the country, and ArgumentException when the country has no subdivisions or the
		// definition is invalid.
		Task<Versioned<SubdivisionInfo>> CreateSubdivisionAsync( SubdivisionDefinition definition );

		// Requires MODIFY. Replaces the definition of the subdivision named by (Country, Code).
		// Throws UnknownCountryException or UnknownSubdivisionException.
		Task<Versioned<SubdivisionInfo>> UpdateSubdivisionAsync( SubdivisionDefinition definition );

		// Requires RETIRE. Leaves the subdivision's localities as they are. Throws
		// UnknownCountryException or UnknownSubdivisionException.
		Task<Versioned<SubdivisionInfo>> RetireSubdivisionAsync( string country, string subdivision );

		// Requires RETIRE.
		Task<Versioned<SubdivisionInfo>> ReinstateSubdivisionAsync( string country, string subdivision );

		// Requires CREATE. Throws UnknownCountryException, UnknownSubdivisionException,
		// DuplicateEntryException when the country already holds the key, and ArgumentException when
		// the postcode is missing though required, does not match the country's PostcodePattern, or
		// the definition is otherwise invalid.
		Task<Versioned<LocalityInfo>> CreateLocalityAsync( LocalityDefinition definition );

		// Requires RETIRE. Throws UnknownCountryException or UnknownLocalityException.
		Task<Versioned<LocalityInfo>> RetireLocalityAsync( string country, string subdivision, string postcode, string name );

		// Requires RETIRE.
		Task<Versioned<LocalityInfo>> ReinstateLocalityAsync( string country, string subdivision, string postcode, string name );

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
		// - each row is one locality, matched by its full key: created when missing (Registration
		//   at the import's timestamp), otherwise its LastUpdate is re-stamped with the import's
		//   context, and it is reinstated when retired; a row differing from the record only in
		//   the casing or spacing of its name matches it and keeps the record's display name;
		// - every locality of the country whose latest of Registration and LastUpdate is earlier
		//   than the import's timestamp is unwanted and deleted, those added by an administrator
		//   included.
		// Always bumps the dataset version once, and publishes the ImportRun (not LocalityChanges)
		// once.
		// Throws UnknownCountryException, UnknownDatasetImporterException, and DatasetImportException
		// when the dataset is unreadable or any row is invalid; a failed import applies nothing, bumps
		// nothing and publishes nothing.
		Task<ImportRun> ImportAsync( string country, ImportRequest request, Stream source );

		// Requires IMPORT. The country's import runs, most recent first. Throws
		// UnknownCountryException.
		Task<IEnumerable<ImportRun>> GetImportRunsAsync( string country );
	}
}
