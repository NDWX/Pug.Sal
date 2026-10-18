# `IDataSession` method contract

This file repeats the comments in `src/Pug.Sal.Data.Abstractions/IDataSession.cs`, together with the rules in DECISIONS D13, D15 and D19. If the two disagree, the source at your package's tag wins.

**General rules:**

- A method addressing a record that doesn't exist does nothing and returns `false` (or `null`).
- `Insert*` throws `DuplicateKeyException` when the key exists, whether the record is retired or not.
- Every `DateTime` returned is UTC (`DateTimeKind.Utc`).
- Records are the full `Country`, `Subdivision`, `Locality`, `Category` and `CountryCategory`, with their audit contexts.

## Countries

| Method | Contract |
|---|---|
| `InsertCountryAsync( Country )` | Insert, with `CurrentVersion` as given. |
| `GetCountryAsync( code )` | The country, or null. |
| `GetCountriesAsync( includeRetired )` | Ordered by code. |
| `GetCountryVersionsAsync()` | Every country, retired ones included, as `CountryVersion`, ordered by code. |
| `UpdateCountryAsync( CountryDefinition, lastUpdate )` | Replace the definition columns, but never the code. Stamp `LastUpdate`. |
| `SetCountryRetiredAsync( code, retired?, lastUpdate )` | Set or clear (when `retired` is null) `Retired`. Stamp `LastUpdate`. |
| `AdvanceVersionAsync( code, utcNow )` | Take the country row's write lock for the rest of the transaction, **then** read the current version. Write `DatasetVersions.Next( current, utcNow )` and return it, or null for an unknown country. Concurrent callers are serialized and each gets a distinct version. |

## Global categories

| Method | Contract |
|---|---|
| `InsertCategoryAsync( Category )` | Insert. |
| `GetCategoryAsync( code )` | The category, or null. |
| `GetCategoriesAsync( includeRetired )` | Ordered by code. |
| `UpdateCategoryAsync( CategoryDefinition, lastUpdate )` | Replace `Name`. Stamp `LastUpdate`. |
| `SetCategoryRetiredAsync( code, retired?, lastUpdate )` | Set or clear `Retired`. Stamp `LastUpdate`. |

## Country categories

| Method | Contract |
|---|---|
| `InsertCountryCategoryAsync( CountryCategory )` | Insert the assignment (`country`, `category`, `disabled`, audit). `Name` is not stored. |
| `GetCountryCategoryAsync( country, category )` | The assignment, with `Name` read from the global category, or null. |
| `GetCountryCategoriesAsync( country, includeDisabled )` | Ordered by category code, each with the global `Name`; disabled ones only when `includeDisabled`. |
| `SetCountryCategoryDisabledAsync( country, category, disabled?, lastUpdate )` | Set or clear `Disabled`. Stamp `LastUpdate`. |
| `DeleteCountryCategoryAsync( country, category )` | Delete. Returns whether a row was deleted. |

## Subdivisions

| Method | Contract |
|---|---|
| `InsertSubdivisionAsync( Subdivision )` | Insert. |
| `GetSubdivisionAsync( country, code )` | The subdivision, or null. |
| `GetSubdivisionsAsync( country, includeRetired )` | Ordered by code. |
| `UpdateSubdivisionAsync( SubdivisionDefinition, lastUpdate )` | Replace `Name` and `Type`. Stamp `LastUpdate`. |
| `SetSubdivisionRetiredAsync( country, code, retired?, lastUpdate )` | Set or clear `Retired`. Stamp `LastUpdate`. |

## Localities

The key is `LocalityKey( Country, Subdivision?, PostcodeKey?, NameKey )`. The caller has already normalized it, and the store never normalizes. A null `Subdivision` or `PostcodeKey` is stored as `''`.

| Method | Contract |
|---|---|
| `InsertLocalityAsync( Locality, postcodeKey?, nameKey )` | Insert under the given keys. `stamped` = `Registration.Timestamp`. |
| `GetLocalityAsync( LocalityKey )` | The locality, or null. |
| `GetLocalitiesAsync( country, subdivision?, postcodeKey?, nameKey?, category?, includeRetired )` | Each filter is applied only when given, as an exact match. Ordered by subdivision, then name key, then postcode key. |
| `SearchLocalitiesAsync( country, namePrefixKey, subdivision?, category?, limit )` | Current localities whose name key **starts with** the prefix, taken literally (wildcards match only themselves). Optionally restricted to one subdivision and one category. Ordered by name key, then subdivision, then postcode key. At most `limit` rows. A store may append fuzzy matches after the prefix matches (Postgres: `pg_trgm`). |
| `SetLocalityRetiredAsync( key, retired?, lastUpdate )` | Set or clear `Retired`. Stamp `LastUpdate`, and set `stamped` = `lastUpdate.Timestamp`. |
| `DeleteLocalityAsync( key )` | Delete. Returns whether a row was deleted. |
| `HasLocalitiesAsync( country, category? )` | Whether any locality exists for the country, retired ones included; only those with the category when it is given. |
| `ReplaceLocalityCategoryAsync( country, from, to, lastUpdate )` | Every locality of the country with category `from`, retired ones included, gets `to`. Stamp `LastUpdate`, and set `stamped` = `lastUpdate.Timestamp`. Returns the count. |

## Imports

| Method | Contract |
|---|---|
| `UpsertImportedLocalityAsync( Locality, postcodeKey?, nameKey, context )` | Key missing: insert it (`Registration` = context, `LastUpdate` null, current) and return `Added`. Key present: set its category to the row's, stamp `LastUpdate` = context, and return `Reinstated` when it was retired (clearing `Retired`), `Recategorized` when it was current under another category, or `Unchanged`. In every case `stamped` = `context.Timestamp`. The caller passes each key only once per import. |
| `DeleteLocalitiesStampedBeforeAsync( country, timestamp )` | `delete … where country = @c and stamped < @t`. Returns the count. |
| `InsertImportRunAsync( ImportRun )` | Insert. |
| `GetImportRunsAsync( country )` | Most recent first, by `DatasetVersion`, which is also the `Performed` timestamp. |

## Stored forms

| Column | Form |
|---|---|
| `country.currentVersion`, `importRun.datasetVersion`, `locality.stamped`, every `retired`, `countryCategory.disabled` | Text in `DatasetVersions.TextFormat` (`yyyyMMddHHmmssfffffff`, UTC), in a bytewise collation. |
| `registration`, `lastUpdate`, `performed` | JSON text of `ActionContext<Reference>` (ISO-8601 timestamp with `Z`). |
| Codes and keys | Text, compared and sorted bytewise (ordinal). |
| `locality.subdivision`, `locality.postcodeKey` | `not null`, `''` for none. |
| Booleans (`postcodeRequired`, `hasSubdivisions`) | The engine's boolean, or an integer 0/1. |
