---
name: sal-administration
description: Use when maintaining Pug.Sal's reference data through ILocalityAdministration, in-process or with LocalityAdministrationClient over HTTP. Covers creating, updating, retiring and reinstating countries, subdivisions and global locality categories; assigning, disabling (with replacement), enabling and unassigning a country's categories; creating, retiring, reinstating and deleting localities; bulk-importing a country's dataset and reading import history; setting up a new country; and the audit fields and change events writes produce.
---

# Maintaining Pug.Sal's reference data

`ILocalityAdministration` (`Pug.Sal.Abstractions`) is Sal's write API, plus the reads an administrator needs. It is implemented in-process by `LocalityAdministration` and over HTTP (`administration/…` unless the host chose another route prefix) by `LocalityAdministrationClient` (`Pug.Sal.Client`). Both have the same contract and the same exceptions. Wire the client exactly like `LocalityDirectoryClient` (see `sal-directory`): `new LocalityAdministrationClient( httpApiClient, routePrefix )`, where `routePrefix` is the host's administration prefix (default `administration`).

Its records are the full `Country`, `Subdivision`, `Locality`, `Category` and `CountryCategory`, with **`Registration`** and **`LastUpdate`** (`ActionContext<Reference>`: actor and UTC timestamp). Its reads behave like the directory's.

## The model in one paragraph

A **country** (`CountryDefinition`: alpha-2 `Code`, `Alpha3`, `Name`, `PostcodePattern`, `PostcodeRequired`, `HasSubdivisions`) holds **subdivisions** (`SubdivisionDefinition`: `Country`, maintainer-defined `Code`, unique within the country and without it, so `NSW` rather than `AU-NSW` (the ISO 3166-2 subdivision part, recommended), `Name`, `Type` from `SubdivisionTypes`), which hold **localities**. A locality (`LocalityDefinition`: `Country`, `Subdivision`, `Postcode`, `Name`, `Category`) is **one locality/postcode pair**. Its identity is `(country, subdivision, normalized postcode, normalized name)`, with no surrogate id. A suburb with two postcodes is two records. Countries and subdivisions are **never deleted, only retired**. Localities are **never updated**: they are created, retired, reinstated or deleted, so **a correction is a delete plus a create**. The one exception is a locality's **category**: every locality has one, taken from a **global list** (`CategoryDefinition`: `Code`, `Name`), which must be **assigned to its country and enabled**. A country always has at least one enabled category. A category in use can't be unassigned, only disabled, optionally moving its localities to another category. There is no history: Sal keeps current state and audit stamps only.

## Operations and permissions

Every call needs the permission it names, on the server's `ISecurityManager`. Over HTTP, it also needs the host's administration scope or policy, if one is set.

| Permission | Calls |
|---|---|
| `VIEW` | `GetCountries`, `GetCountryVersions`, `GetCountry`, `GetSubdivisions`, `GetSubdivision`, `GetCountryCategories`, `GetLocalities`, `GetLocality`, `SearchLocalities` |
| `CREATE` | `CreateCountryAsync`, `CreateSubdivisionAsync`, `CreateLocalityAsync` |
| `MODIFY` | `UpdateCountryAsync`, `UpdateSubdivisionAsync`, which replace the definition (the code is the key and can't change); `AssignCategoryAsync`, `UnassignCategoryAsync` |
| `RETIRE` | `Retire…Async` / `Reinstate…Async` for countries, subdivisions and localities; `DisableCategoryAsync`, `EnableCategoryAsync` |
| `DELETE` | `DeleteLocalityAsync`, which returns the new dataset version |
| `IMPORT` | `ImportAsync`, `GetImportRunsAsync` |

These are all checked on the `COUNTRY` resource. **The global category list is checked on the `CATEGORY` resource** instead, so a host can grant its upkeep separately: `VIEW` for `GetCategoriesAsync` / `GetCategoryAsync`, `CREATE` for `CreateCategoryAsync`, `MODIFY` for `UpdateCategoryAsync` (its name), and `RETIRE` for `RetireCategoryAsync` / `ReinstateCategoryAsync`.

## How writes behave

- **Every write bumps the country's dataset version once,** in the same transaction, and returns the written record with that version (`Versioned<T>.DatasetVersion`, or `Country.CurrentVersion`). Versions strictly increase per country. Writes to one country are serialized. On Postgres, different countries proceed in parallel, while SQLite serializes every write.
- **Retiring is idempotent.** Retiring a retired record, or reinstating a current one, returns it unchanged, bumps nothing and publishes nothing.
- **Retiring a country or subdivision leaves its children as they are.** Validation then reports them under a retired parent, and they aren't retired themselves.
- **Retire or delete a locality?**
  - **Retire** when it should be recognised but discouraged: validation reports `RETIRED`, and the record can be reinstated.
  - **Delete** when it was never right: validation reports `UNKNOWN`.
- **The actor is the caller's identity,** recorded on `Registration` and `LastUpdate`. A service calling with client credentials is recorded as the service. To attribute writes to a person, call with that user's token.
- **After commit,** each write is published to the host's change publisher as a `LocalityChange`, best-effort (see `sal-change-publisher`).
- **Validation of definitions:**
  - `UpdateCountryAsync` refuses to change `HasSubdivisions` while the country holds localities.
  - `CreateSubdivisionAsync` refuses a country without subdivisions.
  - `CreateLocalityAsync` checks the subdivision exists, the postcode against `PostcodeRequired` / `PostcodePattern`, and that the category is assigned to the country (`UnknownCategoryException` otherwise) and enabled. The pattern is matched against the normalized postcode, which is trimmed and upper-cased, and must match it in full.
  - A failed check is an `ArgumentException`.

## Categories

- **Global categories** are created (`CreateCategoryAsync`), renamed (`UpdateCategoryAsync`), retired and reinstated, never deleted. A retired category can't be newly assigned, or enabled again in a country; countries that have it keep it. Global changes bump no country's version, and publish a `LocalityChange` with `EntityType = CATEGORY` and a null `Country`.
- **A country's categories** (`GetCountryCategoriesAsync`, each with the global `Name` and `Disabled`) are country writes, versioned and published like any other (`EntityType = COUNTRY_CATEGORY`):
  - `AssignCategoryAsync( country, category )`: assigns a current global category, enabled. Assigning one already assigned changes nothing.
  - `DisableCategoryAsync( country, category, replacement? )`: new localities and import rows can no longer use it. **With a replacement** (another enabled category of the country), every locality holding it, retired ones included, moves to the replacement in the same write, with `LastUpdate` re-stamped. Without one, they keep the disabled category; call again with a replacement later to move them.
  - `EnableCategoryAsync( country, category )`: refused when the global category is retired.
  - `UnassignCategoryAsync( country, category )`: only while no locality of the country holds it.
  - **A country keeps at least one enabled category:** disabling or unassigning the last one is refused (`ArgumentException`).

## Setting up a new country

An import never creates countries, subdivisions or categories, so create them first. A new country names its first categories (at least one) in `CountryDefinition.Categories`, which only `CreateCountryAsync` reads:

```csharp
await admin.CreateCategoryAsync( new CategoryDefinition { Code = "SUBURB", Name = "Suburb" } );
await admin.CreateCategoryAsync( new CategoryDefinition { Code = "TOWN", Name = "Town" } );

await admin.CreateCountryAsync( new CountryDefinition
	{
		Code = "AU", Alpha3 = "AUS", Name = "Australia", PostcodePattern = @"\d{4}", PostcodeRequired = true, HasSubdivisions = true,
		Categories = new[] { "SUBURB", "TOWN" }
	} );

foreach( ( string code, string name ) in new[] { ( "NSW", "New South Wales" ), ( "VIC", "Victoria" ) /* … */ } )
	await admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = "AU", Code = code, Name = name, Type = SubdivisionTypes.State } );

ImportRun run = await admin.ImportAsync( "AU",
	new ImportRequest
	{
		Importer = "csv", Source = "AU localities 2026-10",
		Options = new Dictionary<string, string> { [ "defaultCategory" ] = "SUBURB" }   // for rows without a category
	},
	File.OpenRead( "au-localities.csv" ) );
```

- **Placement is your call.** Whether a territory is its own country or a subdivision (for example AU's external territories) is the maintainer's choice, and Sal doesn't enforce ISO codes for subdivisions.
- **Write the pattern without anchors.** Sal anchors it, matching `^(?:pattern)$`, so `\d{4}` is enough.

## Imports

`ImportAsync( country, ImportRequest { Importer, Source, Options }, Stream )` reads the dataset with the named importer, which must be registered by the host (see `sal-dataset-importer` for the formats). It **makes the country's localities exactly what the dataset holds**, in one transaction (DECISIONS D13):

- **Present in the dataset but missing in Sal:** created. **Retired in Sal:** reinstated. **Already current:** re-stamped (`LastUpdate` = the import).
- **Categories:** every row needs one (the CSV importer reads a `category` column, or the `defaultCategory` option), assigned to the country and enabled. A locality the dataset holds under another category takes the dataset's (counted in `Recategorized`). A key repeated with a different category fails the import.
- **In Sal but absent from the dataset:** **deleted, including localities an administrator added by hand.** A dataset must cover the whole country. Partial or delta files will delete everything they leave out.
- **Spelling of names that already match:** a row differing from an existing record only in case or spacing matches it, and the record keeps its display name.
- **Subdivisions are never touched.** A row naming an unknown subdivision fails the whole import. A row under a retired subdivision is imported, and the subdivision stays retired.
- **All or nothing.** Any invalid row (missing name, missing or extra subdivision, bad or missing postcode, missing, unassigned or disabled category), an unreadable file, or an empty dataset throws `DatasetImportException`. Its `Position` is usually the 1-based line or row. **Nothing is applied, no version is bumped and nothing is published.** Fix the data and re-import.
- **Result:** the returned `ImportRun` carries `Added`, `Reinstated`, `Recategorized`, `Deleted`, `Unchanged`, `DatasetVersion` and `Performed`. It is stored (see `GetImportRunsAsync( country )`, newest first), and published once as the import's event. Rows publish no individual changes.
- **The country is locked for the duration.** Other writes to it wait until the import ends. The whole dataset is held in memory, and the request is subject to the host's body-size limit over HTTP.
- **No dry run.** Before importing a new source edition into production, import it into a non-production copy and check the counts, above all `Deleted`.
- **Over HTTP,** the client streams the file as `application/octet-stream`, and sends `importer`, `source` and each option as `option.<name>` query parameters.

## Errors

The same exceptions are thrown in-process and over HTTP:

| Exception | When | HTTP |
|---|---|---|
| `AccessDeniedException` (an `UnauthorizedAccessException`; `Operation` names the refused permission in-process) | Missing permission, or a failed host scope or policy | 403 |
| `AuthenticationException` | No or invalid credentials (HTTP only) | 401 |
| `UnknownCountryException` / `UnknownSubdivisionException` / `UnknownLocalityException` | The record addressed doesn't exist | 404 |
| `UnknownCategoryException` | The global category doesn't exist (`Country` null), or isn't assigned to the country (`Country` set) | 404 |
| `DuplicateEntryException` | The code or key already exists, retired or not. Reinstate it instead | 409 |
| `ArgumentException` | Invalid definition, a postcode failing the country's rules, a blank key part, a disabled or retired category, unassigning a category in use, or leaving a country without an enabled category | 400 |
| `UnknownDatasetImporterException` | `ImportRequest.Importer` isn't registered | 422 |
| `DatasetImportException` | Unreadable or invalid dataset (see `Position`) | 422 |

## Reading audit data

`Registration` and `LastUpdate` say who created a record and who last changed it (retired, reinstated, updated, or confirmed by an import), and when. `Retired` is the retirement time, and null when the record is current. For a full trail of changes, consume the change events. Sal keeps no history.
