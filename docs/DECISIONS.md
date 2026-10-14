# Pug.Sal — Design Decisions

Decision log for Pug.Sal (approved plan: `~/.claude/plans/compiled-spinning-turtle.md`). Each entry
records the decision and the reasoning, so work can resume without the original conversation.

## Context

Sal maintains the countries, their states/territories (subdivisions), suburbs (localities) and
postcodes. It is the single source of truth that internal applications validate user address input
against. Agreed shape (2026-10-07):

- **Internal use only.** Sal is not a data-redistribution service and not a paid service. Whether a
  dataset may be loaded is therefore the **host's concern**: Sal carries no licence or attribution
  model, and importers do not judge their source files.
- **Australia first.** Other countries follow through the same importer family.

## D1 — Package shape follows Pug.Scheduling

`Models / Abstractions / core / Data.Abstractions / Data.SQLite / Data.Postgres / Importers.* /
WebApi.Models / WebApi / Client / DependencyInjection`. A consumer hosts Sal in-process through DI,
or out-of-process behind `MapSalEndpoints()` and binds the same interfaces to `Pug.Sal.Client`. The
client reproduces the facades' exceptions, so callers cannot tell local from remote. Contracts
(Models, Abstractions, WebApi.Models, Client) multi-target `net6.0;net8.0;net10.0`, and
implementations are `net10.0`.

## D2 — Fixed three-level model (user choice)

Country (ISO 3166-1: alpha-2 is the key, alpha-3 is also stored) → Subdivision (code
maintainer-defined, ISO 3166-2 recommended, e.g. `AU-NSW`; see D10) → Locality. Postcode ↔ locality is many-to-many. An arbitrary-depth hierarchy was
considered and rejected: it fits AU/NZ/US and most countries without one, and it keeps the queries
flat.

Identity rules that follow from real data:
- One postcode can span several localities **and several subdivisions** (AU 2620 covers NSW and
  ACT).
- One locality can have several postcodes.
- The same locality name can appear in several subdivisions.
- Locality identity is therefore `(country, subdivision, normalized postcode, normalized name)`,
  never the name alone. There is **no surrogate identifier** (D13).

`CountryInfo` records the country's shape (`PostcodePattern`, `PostcodeRequired`,
`HasSubdivisions`), so validation can work for countries that lack postcodes or subdivisions.

## D3 — Query surface: validate, lookup/list, search (user choice)

- **List/lookup:** countries; a country's subdivisions; localities by postcode; postcodes by
  locality.
- **Search/autocomplete:** prefix search on the normalized locality name within a country, which
  can be narrowed to a subdivision. Postgres adds `pg_trgm` similarity for fuzzy matching. SQLite is
  prefix-only, and that difference is documented, not hidden.
- **Validate:** given an `AddressCandidate` (country, subdivision, locality, postcode), check each
  component on its own and then the combination. The result reports:
  - the outcome per component;
  - the canonical spelling and casing;
  - suggestions (e.g. the postcodes the locality actually has, or the subdivision the
    locality/postcode pair belongs to).

  Sal **never auto-corrects silently**. The calling application decides what to do with a
  suggestion.

## D4 — Name normalization

`INameNormalizer`: trim, collapse internal whitespace, case-fold (invariant) and strip diacritics.
The normalized form is **stored** next to the display name, so matching and prefix search are
index lookups and not per-row computation. Per-country abbreviation rules (`St` ↔ `Saint`, `Mt` ↔
`Mount`) are an extension point and are not in v1.

Postcodes are part of the locality key too, so `NormalizePostcode` trims, collapses whitespace and
upper-cases (`sw1a  1aa` matches `SW1A 1AA`).

## D5 — No history; retirement, and deletion of localities (user directive; revised by D13)

Full history (effective dating) is **not** required.
- Countries and subdivisions are retired (`Retired` UTC timestamp), never deleted. A retired record
  can be reinstated.
- Localities can be retired too, and they can also be **deleted**, by an administrator or by an
  import (D13).
- Validation reports a match on a retired record as `RETIRED`. A deleted locality is simply
  `UNKNOWN`.
- For an audit trail, see D7.

## D6 — Per-country dataset version for client-side caching (user directive)

- `CountryInfo.CurrentVersion` is a UTC timestamp, bumped in the **same transaction** as every
  insert, update, retire, reinstate or import that touches the country. An import bumps it once,
  not once per row.
- **Strictly increasing:** the new value is `max( utcNow, previous + 1 tick )`. Two writes in the
  same tick, or a host clock that went backwards, therefore cannot give one version two meanings.
  - `DatasetVersions.Next( previous, utcNow )` in `Pug.Sal.Data.Abstractions` is the single
    implementation, shared by both stores.
- **Stored as numeric text (user directive, 2026-10-09).** Every version-valued column
  (`country.currentVersion`, `importRun.datasetVersion`, `locality.stamped`) holds the UTC
  timestamp's digits to the tick, `yyyyMMddHHmmssfffffff` (`DatasetVersions.TextFormat`, e.g.
  `202610091030001234567`).
  - It is fixed width, so text order is time order, and exact to the tick in every store.
  - That replaces a brief one-microsecond step chosen for Postgres `timestamptz` precision. The
    version no longer depends on a store's own timestamp type.
  - The API still carries the version as a UTC `DateTime`. `ToText`/`FromText` convert.
- **Every read result carries the version it was read at** (`Versioned<T>`, and
  `ValidationResult.DatasetVersion`). The version is read in the same session as the data, so it
  never runs ahead of the data it describes.
- Clients revalidate cheaply with `GetCountryVersionsAsync`, or over HTTP with
  `ETag`/`If-None-Match` (304). A ready-made caching client is a later addition. The contract only
  has to make one possible.

## D7 — Host-supplied event broker for audit (user directive)

There is no event-broker abstraction anywhere in the Pug ecosystem, so Sal defines
`ILocalityChangeEventPublisher` in Abstractions. The host may register one, and if it does not, a no-op
publisher is used.

- After commit, every write publishes `LocalityChange { Country, EntityType, Key, Kind, Actor
  (ActionContext<Reference>), DatasetVersion }`.
- **An import is a different event (user directive, 2026-10-09).** It publishes its `ImportRun`
  through `ILocalityChangeEventPublisher.PublishImportAsync`, once, after commit. The event carries the
  identifier, country, importer, source, the counts `Added`, `Reinstated`, `Deleted` and
  `Unchanged`, the dataset version and the performer.
  - Its rows publish no `LocalityChange`, so `ChangeEntityTypes.IMPORT` and `ChangeKinds.IMPORTED`
    are gone.
  - The `ImportRun` record doubles as the payload, so the stored run and the event cannot drift
    apart.
- Delivery is **best-effort after commit**. A publisher failure is logged and never rolls back or
  fails the write. Audit is optional, so it must not be able to take the write path down. A
  transactional outbox was considered and deferred; it can be added later behind the same
  interface.

## D8 — Every operation requires authentication and authorization (user directive)

- **Reads included.** Facades authorize through the `ISecurityManager.CurrentPrincipal.
  IsAuthorizedAsync( context, operation, NounQualifier, purpose )` path from
  Pug.Application.Security, so any authentication and authorization mechanism works.
- **No principal means refused.** Pug.Scheduling's coordinator lets an in-process call that has no
  principal skip the check; Sal deliberately does not. An in-process consumer must carry a
  principal, just as a remote one does.
- **Endpoints** only require an authenticated caller and map `NotAuthorized` / `Unknown*Exception` /
  `ArgumentException` to 403/404/400. The facades decide. A host may add per-surface scopes and a
  policy in front of them (D23).
- **Operations** (`SecurityOperations`): `VIEW`, `CREATE`, `MODIFY`, `RETIRE`, `IMPORT`. They live
  in Abstractions, following the Scheduling precedent (D3 there).
- **Resource: all countries only, for now (user directive).** Every check is made against
  `ResourceQualifiers.AllCountries` = `{ Type = COUNTRY }`, unqualified like Scheduling's
  `AnySchedule`. `Domain` is empty, because Sal's reference data is global, not tenant-scoped.
  Operations that list countries are refused like any other when `VIEW` is not granted. Nothing is
  filtered by grant.
- **Country-specific permissions are deferred (user directive, 2026-10-07).** The user wants a
  per-country deny to be able to override an all-countries grant. The
  `Pug.Application.Security` `IsAuthorizedAsync` contract is boolean. Pug.Authorized does
  distinguish Allowed, Denied and None, but its adapters return only `== Allowed`. So Sal cannot
  tell "denied for AU" from "no AU-specific entry". If per-country permissions return, the chosen
  model is **the provider resolves it**:
  - Sal makes one check against `{ Type = COUNTRY, Identifier = <alpha-2> }`.
  - The host's provider checks the country-specific entry first (a deny wins), then falls back to
    the all-countries entry. That is Pug.Authorized's native resolution order.

  Two alternatives were rejected:
  - Exclusion grants: a separate `COUNTRY_EXCLUSION` resource.
  - A three-state evaluator inside Sal: it would go around the Pug.Application.Security
    abstraction.
- **First host:** Entra ID. Authorization may come from Entra app roles via
  `Pug.Application.Security.AzureADRoleProvider`, which the **host** wires behind its
  `IAuthorizationProvider`. Sal does not reference it.

## D9 — Importers are a pluggable family

`IDatasetImporter` (keyed by name, `OrdinalIgnoreCase`) turns a source stream into records for one
country. `IDatasetImporterRegistry` ships empty, and the host registers what it uses.
`ImportCoordinator` applies one import to one country in a **single transaction** and records an
`ImportRun` (importer, source label, dataset version, counts, actor). What the import does to the
data is D13. The Merge/Replace modes first planned here were dropped.

v1 ships a generic column-mapped `Pug.Sal.Importers.Csv`. Source-specific importers are separate
packages.

## D10 — Australia specifics

- Postcode is `^\d{4}$` and required.
- Subdivisions are the six states plus ACT and NT (ISO 3166-2:AU).
- **Territories are the data maintainer's call (user directive).** Sal takes no position on the
  external territories (Norfolk Island, Christmas Island, Cocos (Keeling) Islands; ISO 3166-1
  NF/CX/CC, AU postcodes 2899/6798/6799) or Jervis Bay Territory (postcode 2540, no ISO 3166-2
  code). The model must allow any choice the maintainer makes:
  - A territory can be its own country (an `NF` country with a `^\d{4}$` postcode pattern), or a
    subdivision or localities under `AU`.
  - Subdivision codes are therefore **maintainer-defined**. ISO 3166-2 is the recommended
    convention, not a check Sal enforces, so a code like `AU-JBT` is accepted.
  - Postcodes are unique only within a country, so `AU` 2899 and `NF` 2899 can coexist.
  - The AU fixture and CSV importer bake in no territory choice.

## D11 — Stores

- Dapper over `Pug.Application.Data` (`IApplicationData<IDataSession>`). The SQLite and Postgres
  stores are kept in step.
- All times are UTC. SQLite reads stamp `DateTimeKind.Utc` to match Postgres `timestamptz`
  (Scheduling D18 found this defect after the fact, so it is designed in here).
- The schema ships as `GetSchemaUpgradeScripts()` for DbUp.

## D12 — Contract decisions made in phase 1

- **"Locality" names the service surface (user directive, 2026-10-09).** The term "Geography" is
  dropped: `ILocalityDirectory` (reads), `ILocalityAdministration` (writes),
  `ILocalityChangeEventPublisher` and `LocalityChange` (the admin write event). They cover countries
  and subdivisions as well as localities; `LocalityChange.EntityType` says which.
- **Definition + Info composition**, following Scheduling's `ScheduleInfo`:
  - `XDefinition` is what a maintainer authors. `XInfo` adds what Sal maintains: `Retired`,
    `Registration`, `LastUpdate`, plus `CurrentVersion` on countries.
  - **Revised by D16:** `XInfo` no longer carries `Registration` / `LastUpdate`. The full record
    `X` (`Country`, `Subdivision`, `Locality`) does.
  - Writes take definitions and return the written record. Country-scoped writes wrap it in
    `Versioned<T>` with the version the write produced.
- **No `PostcodeInfo`.** The plan listed one. Instead, a postcode is a plain string, unique within
  its country, and each locality record carries **one** postcode as part of its key (D13):
  - A suburb served by several postcodes is one record per postcode.
  - "Postcodes of a locality" is `GetLocalitiesAsync( name: … )`. "Localities of a postcode" is
    `GetLocalitiesAsync( postcode: … )`.
- **Imports are flat rows.** An `ImportRow` is one locality/postcode pair, which is what postcode
  datasets look like, and so exactly one locality record. A row repeating an earlier row's key is
  the same locality.
  - **An import never creates, retires or deletes a subdivision (user directive, 2026-10-09).** A
    row naming an unknown subdivision fails the **whole** import (`DatasetImportException`):
    nothing is applied, bumped or published. Subdivisions are maintained through the admin API
    before importing. `ImportRow` therefore has no subdivision name or type.
  - **A retired subdivision does not fail the import (user directive, 2026-10-09).** Its rows are
    imported like any other, and the subdivision stays retired. The import does not reinstate it.
    Validation reports such an address with a `RETIRED` subdivision.
  - Every other invalid row fails the whole import the same way: a subdivision missing where the
    country `HasSubdivisions` (or present where it does not), a postcode missing where required,
    or one not matching `PostcodePattern`.
- **Validation of an unknown country throws** `UnknownCountryException`, as every country-scoped
  operation does, rather than returning a country outcome of `UNKNOWN`. Consumers choose the
  country from Sal's own list.
- **Creating a duplicate throws** `DuplicateEntryException` (409 over HTTP), retired records
  included: reinstate those instead. Retire and reinstate share the `RETIRE` operation.
- **No cancellation tokens** on the facades or the publisher, matching Scheduling's contracts.

## D13 — Localities keyed by name; imports make the country match the dataset (user directive)

The user's proposal, reviewed for gaps (including a review from another session), then settled
item by item.

- **No locality identifier; the key is `(country, subdivision, postcode, name)`** (user
  directive). Postcode and name are compared in their normalized forms
  (`INameNormalizer.NormalizePostcode` / `Normalize`).
  - A locality record is one locality/postcode pair, the same shape as a dataset row.
  - A suburb served by several postcodes is several records.
  - Same-named places within one subdivision (the review believed Queensland has several "Spring
    Creek"s) are told apart by postcode, and can be retired or deleted separately.
  - **Localities are never updated** (user directive). They are created, retired, reinstated or
    deleted, so a correction (rename, re-postcode, move) is a delete and a create. `MODIFY` and
    `UPDATED` apply to countries and subdivisions only.
  - An import matches rows by the full key. A matched locality has its `LastUpdate` re-stamped
    and is reinstated if retired. A row differing only in the casing or spacing of its name
    matches the existing record; the display name stays as first created.
  - Validation answers with canonical `AddressCandidate`s.
- **Localities can be added, retired, reinstated and deleted** (`SecurityOperations.Delete`), but
  not updated. Every one of those writes bumps the country version (D6). `Registration` and
  `LastUpdate` are kept. `LastUpdate` records the last retirement, reinstatement, or import that
  confirmed the locality.
- **Imports always cover the whole country.** Whatever the dataset does not hold is **deleted**:
  - Localities absent from the dataset are deleted, not retired. This reverses the first plan.
  - That includes localities an administrator added by hand. The dataset is the country's single
    source of truth.
  - A locality the dataset holds that an administrator had retired is **reinstated**.
  - Never import a dataset covering only part of a country: it would delete the rest.
  - Imports never create, retire, reinstate or delete subdivisions. An unknown one fails the whole
    import; a retired one is accepted and stays retired (D12).
- **Unwanted rows are found by timestamp (user directive, 2026-10-09).** This replaces the
  assistant's earlier run-stamp recommendation. The import upserts:
  - A new locality is registered at the import's timestamp.
  - Every existing locality the dataset holds, unchanged or reinstated, has its `LastUpdate`
    re-stamped with the import's context.
  - Every locality of the country whose **latest** of `Registration` and `LastUpdate` is earlier
    than the import's timestamp is unwanted and deleted. The test is the latest of the two, not
    either one: an existing locality's `Registration` always predates the import, even when the
    import re-stamped it.
  - So `LastUpdate` on a locality means "last retired, reinstated or confirmed by an import", not
    "last changed".
- **The import's timestamp is clock-safe (assistant's choice, needed by the directive above).**
  - Every write uses the version it produces as its action timestamp:
    `max( utcNow, previous + 1 tick )` (D6), taken under the country row lock.
  - So every earlier write's timestamp is at or before the country's current version, and the
    import's timestamp is strictly after it.
  - The rule therefore cannot misclassify a row through clock skew between hosts or a clock going
    backwards. An action timestamp may run a tick or so ahead of wall time; that is the cost.
- **Admin writes wait for an import.** The import takes the country row lock first, and every
  other write to the country bumps its version under the same lock, so they wait for it.
- **Every import bumps the version and publishes its import event once** (D7). Even an import that adds, reinstates and
  deletes nothing re-stamps `LastUpdate`, which reads expose, so cached reads must be invalidated.
  This reverses the assistant's earlier "no bump for a no-op import" recommendation.
- **The version stays a strictly increasing UTC timestamp** (D6). The review suggested a counter,
  but the user asked for a timestamp, and the rule above relies on it being one.

## D14 — One class implements both service interfaces (user directive, 2026-10-09; superseded by D16)

Superseded the same day by D16. Kept for the record.

- `ILocalityDirectory` and `ILocalityAdministration` stay separate interfaces, so a consumer can
  depend on reads only. Both are implemented by **one** class, not one class each:
  - `LocalityService` in `Pug.Sal`. Phase 3 writes its read half, and phase 4 its admin half.
  - `LocalityServiceClient` in `Pug.Sal.Client`.
- DI registers that one instance under both interfaces.
- Import mechanics may sit in an internal collaborator (the planned `ImportCoordinator`) that
  `LocalityService` calls. That does not make it a second implementation of either interface.

## D15 — Store design (phase 2, assistant's choices)

- **The store holds no policy.** `IDataSession` does storage primitives only. These stay with the
  caller (`LocalityDirectory` / `LocalityAdministration`, D16): authorization, normalization, validation, version policy, key
  de-duplication within a dataset, and choosing timestamps.
- **The caller supplies normalized keys.** The store never normalizes. A locality is stored with
  its display `postcode` and `name`, plus `postcodeKey` and `nameKey` from `INameNormalizer`.
  `LocalityKey { Country, Subdivision, PostcodeKey, NameKey }` addresses a stored locality.
- **"None" is stored as an empty string in key columns.** `subdivision` and `postcodeKey` hold `''`
  when there is none, and the store maps that back to null. Both are therefore plain
  `not null` primary-key columns, with the same semantics in SQLite and Postgres. Unique indexes
  over nullable columns differ between them.
- **Locking and versioning in one primitive.**
  - `AdvanceVersionAsync( country, utcNow )` takes the country row's write lock first: an
    `update` in both stores, which in SQLite takes the database write lock.
  - It then reads the version, writes `DatasetVersions.Next`, and returns the new version, or
    null for an unknown country.
  - Every write transaction calls it first, then uses the returned value as its action
    timestamp (D13).
- **`stamped` column.** Each locality row keeps the latest of its `Registration` and `LastUpdate`
  timestamps as a plain column, maintained by every write. An import's unwanted-row delete is
  then `where country = @c and stamped < @importTimestamp`, using the `(country, stamped)` index.
  `Registration` and `LastUpdate` themselves are JSON columns, as in Scheduling.
- **Import upsert returns its outcome.** `UpsertImportedLocalityAsync` inserts a missing locality
  or re-stamps an existing one (clearing `Retired`). It returns `Added`, `Reinstated` or
  `Unchanged`, so the caller counts without a separate read.

- **SQLite transactions: the session enlists itself (found in phase 2).**
  - **The problem:** Microsoft.Data.Sqlite ignores ambient `System.Transactions`, so the
    `TransactionScope` that `ExecuteAsync`/`PerformAsync` open gave no atomicity and no write lock.
    Statements autocommitted, and concurrent version bumps returned duplicates.
  - **The fix:** a session created inside a scope opens a real SQLite transaction. An
    `EnlistVolatile` enlistment ties that transaction to the scope's outcome.
    - It commits in **phase one** (`Prepare`), so a failed commit aborts the scope and its
      `Dispose` throws. A commit in phase two would surface only after the scope reported
      success.
    - Closing the connection is deferred until the scope ends, because ApplicationData disposes
      the session first.
  - **Limits:** a nested `Required` scope gets its own connection and transaction. Committing in
    phase one cannot be undone if another participant in the same scope fails, but Sal's units of
    work enlist nothing else.
  - **Postgres (phase 5):** Npgsql enlists in `TransactionScope` natively, so this workaround must
    **not** be copied there. Verify it with a rollback test instead.
  - **Scheduling has the same latent issue:** Pug.Scheduling's SQLite store has the same gap, and
    its writes are not atomic on SQLite. Sal's fix is local to Sal.
- **Timestamps in SQLite:**
  - Version-valued columns use the digit text of D6.
  - `importRun` has no separate performed-time column. Runs are ordered by `datasetVersion`,
    which equals the `Performed` timestamp (D13).
  - Other timestamps (`retired`) use Microsoft.Data.Sqlite's default text form,
    `yyyy-MM-dd HH:mm:ss.FFFFFFF`, in UTC.
  - `ActionContext` timestamps live in its JSON.
- **Duplicate keys and missing parent countries** surface as `SqliteException`, unwrapped. The
  service maps them in phase 4.

## D16 — Separate directory and administration; audit-free reader records (user directive, 2026-10-09)

Reverses D14. The directory should not hand out the audit contexts, so administration needs its own
reads, and the two interfaces get their own implementations.

- **Three tiers of record.**
  - `XDefinition`: what a maintainer authors.
  - `XInfo` (`CountryInfo`, `SubdivisionInfo`, `LocalityInfo`): what `ILocalityDirectory`
    publishes. It holds the definition, `Retired`, and `CurrentVersion` on countries. It has **no**
    `Registration` / `LastUpdate`.
  - `X` (`Country`, `Subdivision`, `Locality`): the full record with the audit contexts, returned by
    `ILocalityAdministration`.
  - The full records are **standalone**: fields are repeated, with no inheritance from `XInfo`
    (user choice). A directory result can then never be a full record at runtime, and polymorphic
    serialization cannot leak the audit fields.
- **The store returns the full records.** `IDataSession` reads and writes `Country` /
  `Subdivision` / `Locality`. The directory projects them with the `ToInfo()` extension methods in
  `Pug.Sal.Models/InfoProjections.cs` (user choice).
- **`ILocalityAdministration` has its own reads**, mirroring the directory's except validation:
  - `GetCountriesAsync`, `GetCountryVersionsAsync`, `GetCountryAsync`;
  - `GetSubdivisionsAsync`, `GetSubdivisionAsync`;
  - `GetLocalitiesAsync`, `GetLocalityAsync`, `SearchLocalitiesAsync`.

  They require **VIEW**, like the directory's (user choice). Hiding the audit fields is about API
  shape, not secrecy. Its writes return the full records.
- **Implementations:**
  - `LocalityDirectory : ILocalityDirectory` and `LocalityAdministration : ILocalityAdministration`
    in `Pug.Sal`. Import mechanics sit in an internal `ImportCoordinator` used by
    `LocalityAdministration`. Shared read logic (key normalization, unknown-country checks) lives
    in internal helpers, not in one class implementing both interfaces.
  - `LocalityDirectoryClient` and `LocalityAdministrationClient` in `Pug.Sal.Client`.
  - DI registers each class under its own interface.
- **The model generator is checked in** as `tools/genmodels.py`. Model fields and comments are
  edited there, and the records are then regenerated with
  `python3 tools/genmodels.py src/Pug.Sal.Models`. `InfoProjections.cs` is hand-written.

## D17 — Read facade and validation (phase 3, assistant's choices)

- **Shared base.** `LocalityServiceBase` (public abstract, following Scheduling's `SchedulerBase`)
  holds what both facades need:
  - authorization;
  - argument normalization;
  - the reads over full records, each checking VIEW.

  `LocalityDirectory` projects the results with `ToInfo()`. `LocalityAdministration` (phase 4)
  returns them as they are. Authorization is checked **before** arguments are validated or
  anything is looked up, so an unauthorized caller learns nothing.
- **Argument forms:**
  - Country codes are trimmed and upper-cased. Subdivision codes in lookups and filters are trimmed
    and matched exactly.
  - Postcode and name filters use their normalized forms. A blank filter means no filter.
  - A blank locality name in a key lookup is an `ArgumentException`.
- **Normalizer** (`NameNormalizer`):
  - Names are trimmed, whitespace is collapsed, diacritics are stripped (FormD, non-spacing marks
    dropped), and the result is upper-cased invariantly.
  - Postcodes are trimmed, whitespace is collapsed, and they are upper-cased. A blank postcode
    normalizes to null.
- **Search:** a limit below 1 is an `ArgumentOutOfRangeException`, and the limit is capped at
  `MaximumSearchLimit` (100). An unknown subdivision matches nothing.
- **Validation** (`AddressValidation`, a pure function over evidence read in one session):
  - **Subdivision** is matched by code, then by the code's part after `<country>-` (so `NSW`
    matches `AU-NSW`), then by normalized name, each case-insensitively.
  - **A blank optional postcode is `NOT_APPLICABLE`.** A blank required one is `MISSING`.
    `PostcodePattern` must fully match the *normalized* postcode.
  - **Combination** is `NOT_CHECKED` unless every component is `VALID`, `RETIRED` or
    `NOT_APPLICABLE`. Otherwise it is:
    - `VALID` when a current locality has the name, the matched subdivision and the given
      postcode;
    - **`RETIRED`** when only retired ones do;
    - `MISMATCH` otherwise.
  - Components that match both current and retired records report the current one.
  - **`Match`** is given only when the result is valid. Its postcode is null when the candidate
    gave none, because filling one in would be a correction.
  - **Suggestions** are current localities under current subdivisions, at most 10, from the first
    of these tiers that has any:
    1. the name with the given postcode (any subdivision), then the name under the matched
       subdivision;
    2. the name anywhere;
    3. the given postcode's localities under the matched subdivision;
    4. the given postcode's localities anywhere.

    A retired country gets no suggestions.

## D18 — Administration, imports and the CSV importer (phase 4, assistant's choices)

- **One transaction per write, lock first.** `LocalityAdministration.WriteAsync` runs each write to
  an existing country in one `ExecuteAsync`:
  - `AdvanceVersionAsync` first, which takes the lock and the version;
  - then the write, stamped with an action context at that version.

  Any exception inside it, such as an unknown subdivision or a duplicate, rolls the bump back with
  the write.
  - A new country has no row to lock. Its first version is `DatasetVersions.Next( MinValue, now )`.
- **Duplicates.** Store inserts throw the store-neutral `DuplicateKeyException`
  (`Pug.Sal.Data.Abstractions`). SQLite maps extended codes 1555 (primary key) and 2067 (unique);
  a foreign-key violation stays a `SqliteException`. The facade reports it as
  `DuplicateEntryException`, with the key as `<subdivision>/<postcode>/<name>` for localities.
- **Retire and reinstate are idempotent.** If the record is already in the requested state, it
  comes back from a plain read, with no bump and nothing published. The check happens before the
  write transaction. A race between two such calls costs at most one redundant, harmless write.
- **Definitions are tidied before storage** (`DefinitionRules`):
  - Country codes must match `^[A-Z]{2}$` after trimming and upper-casing. Alpha-3 must match
    `^[A-Z]{3}$`.
  - Names and codes are trimmed. Blank optional values become null.
  - A `PostcodePattern` that will not compile is an `ArgumentException`.
  - Subdivision codes keep their case.
- **Locality creation:**
  - Allowed under a retired subdivision, as imports are.
  - Display values are trimmed.
  - The postcode is checked by `PostcodeRules`, shared with validation and imports.
- **`HasLocalitiesAsync`** is the store primitive behind refusing a `HasSubdivisions` change.
- **Imports:**
  - The country is checked before the dataset is read. The whole dataset is parsed into memory
    before the transaction starts, so the lock is not held while the source is read.
  - Rows are checked before anything is written. `DatasetImportException.Position` is the 1-based
    row number. A repeated key keeps the first row's display values.
  - **An empty dataset is refused**, because it would delete every locality of the country.
  - `ImportRun.Importer` is the requested importer name, trimmed. `Identifier` is a new GUID (`N`
    format).
- **Publishing** happens after commit through the host's publisher. A failure is logged as a
  warning (`ILogger<LocalityAdministration>`, optional) and swallowed.
  - `NullLocalityChangeEventPublisher` is the default when the host registers none.
  - `DatasetImporterRegistry` is the default registry. It is case-insensitive and refuses duplicate
    names.
- **`Pug.Sal.Importers.Csv` (`CsvDatasetImporter`, conventional name `csv`):**
  - RFC 4180 with a header record. Columns are found by header name (options `locality`,
    `postcode`, `subdivision`), and `subdivisionPrefix` maps `NSW` to `AU-NSW`. The `delimiter`
    option takes a single character or `tab`.
  - Every record must have as many fields as the header. Values are trimmed and blank lines
    skipped.
  - Error positions are 1-based line numbers.
  - It has no third-party CSV dependency.
- **SQLite and long imports:** while an import holds the write lock, other writers wait up to the
  connection's `busy_timeout`. Hosts importing large datasets should set it generously.

## D19 — Postgres store and transaction isolation (phase 5, assistant's choices)

- **Units of work set their own isolation** (`UnitsOfWork` in `Pug.Sal.Data.Abstractions`). Each one
  runs in a transaction of its own (`RequiresNew`), never in a caller's ambient one.
  - **Writes run at ReadCommitted.** Writers to a country serialize on the row lock that
    `AdvanceVersionAsync` takes (`select … for update` in Postgres). Under the default Serializable,
    a writer that waited for that lock fails with "could not serialize access due to concurrent
    update" instead of proceeding.
  - **Reads run at RepeatableRead**, so every statement of one read sees one snapshot. A read-only
    transaction never fails at that level.
  - **Why `RequiresNew`:** joining a caller's scope whose isolation level differs would throw.
    Sal's data is its own, so nothing is lost by not joining.
  - SQLite, with its session's own enlistment (D15), is serializable at any level.
- **The Postgres store is kept in step with SQLite:**
  - It has the same tables, columns, key semantics (`''` for none) and ordering.
  - Codes, keys and version text use the `"C"` collation, so comparisons and ordering are bytewise,
    as in SQLite.
  - Duplicates map from `unique_violation` (23505) to `DuplicateKeyException`. Npgsql enlists
    natively, so there is no enlistment workaround. A rollback test proves atomicity.
- **`retired` is stored as version digit text in both stores.** It always equals an action
  timestamp, which is a version, and Postgres `timestamptz` keeps only microseconds. This extends
  the user's D6 directive to the one remaining timestamp column. The SQLite store changed with it;
  nothing had been deployed.
- **Similarity search (Postgres only, D3):**
  - The schema runs `create extension if not exists pg_trgm`, so the role applying it needs the
    right to do that. pg_trgm is trusted, so the database owner may create it.
  - A GIN trigram index covers `nameKey`.
  - Search returns prefix matches first, then non-prefix matches by `nameKey % prefix` (at
    `pg_trgm.similarity_threshold`), best first, up to the limit.
- **Shared contract suites.** `StoreContractTests<TFixture>` and
  `LocalityAdministrationContractTests<TFixture>` run against both stores:
  - `Sqlite*Tests` in `Pug.Sal.Tests`;
  - `Postgres*Tests` in `tests/Pug.Sal.Postgres.Tests`, which links the shared sources and uses a
    `postgres:17-alpine` Testcontainers container per fixture.

  Search assertions take the leading prefix matches and require any similarity matches to come
  after them.

## D20 — HTTP surface, clients and DI (phase 6, assistant's choices)

- **Routes.**
  - **The directory is under `sal/`:**
    - `countries`, `countries/versions` and `countries/{country}`;
    - `…/subdivisions` and `…/subdivisions/{subdivision}`;
    - `…/localities` (filters as query), `…/localities/search` and `…/locality`;
    - `POST sal/validate`, a POST only because the candidate travels as a body.
  - **Administration mirrors the reads under `sal/admin/`**, with these writes added:
    - `POST countries`, `PUT countries/{c}` and `POST countries/{c}/retire|reinstate`;
    - the same three shapes under `subdivisions`;
    - `POST …/localities`, `POST …/locality/retire|reinstate` and `DELETE …/locality`, which
      returns a `DatasetVersionResult`;
    - `POST …/imports` and `GET …/imports`.
  - A single locality is addressed by query (`subdivision`, `postcode`, `name`), because its
    subdivision and postcode may be absent.
  - Where a route names the country, subdivision or code, the route wins over the body.
  - `MapSalEndpoints()` maps both. A read-only host maps `MapSalDirectoryEndpoints()` alone.
- **Authentication.** Every endpoint calls `RequireAuthorization()` (authenticated caller, default
  policy), so an anonymous caller gets 401 and never reaches a facade. Authorization stays with the
  facades (D8). `EndpointMetadataTests` checks the metadata and an anonymous request to every route.
  A host may require further scopes and a policy, separately per surface (D23).
- **Failures** are mapped by `SalResults` and reversed by the client's `ApiFailures`:
  - 403 for `NotAuthorized`;
  - 400 with a `BadRequestResultInfo` for `ArgumentException`;
  - 404 with a `FailureInfo` for an unknown country, subdivision or locality;
  - 409 with a `FailureInfo` for a duplicate;
  - 422 with a `FailureInfo` for an unknown importer or an invalid dataset (with `Position`).

  401 stays the transport's `AuthenticationException`.
- **ETags on every read:**
  - a country-scoped read is tagged with the ticks of the dataset version it carries;
  - a cross-country listing is tagged with a SHA-256 over its `code:version` pairs.

  Tags are per URL. `If-None-Match` that matches (or `*`) gets 304. The read still runs, so the
  saving is bandwidth, not database work.
- **Imports over HTTP** send the dataset as the raw request body, as `application/octet-stream`,
  `text/csv` or `text/plain` (`SalAdministrationEndpoints.ImportMediaTypes`). Routing refuses any
  other media type with 415 before authentication runs, so even an anonymous caller gets 415. `importer` and
  `source` go in the query, and each `option.<name>=<value>` becomes `Options[name]`. Kestrel's
  request size limit applies; hosts importing large datasets may need to raise it.
- **Clients.** `LocalityDirectoryClient` and `LocalityAdministrationClient` extend Pug.HttpApiClient's
  `ApiClient`. Pug.HttpApiClient's raw calls do not throw on 422, so the client checks the status
  itself.
- **OpenAPI metadata.** Every endpoint has a name, a tag ("Sal" or "Sal administration") and a
  summary. It also declares exactly the statuses it can produce, through the `ResponseMetadata`
  helpers, and request content types where it takes a body. The import's open-ended
  `option.<name>` query parameters are documented in its description.
  - Sal serves no OpenAPI document itself. The host surfaces the metadata, typically in
    development only, with `AddEndpointsApiExplorer()` and `AddSwaggerGen()`, or with
    `AddOpenApi()` and `MapOpenApi()`. This follows Scheduling and Groups.
  - `EndpointMetadataTests` pins all of the above.
- **Target frameworks.** `Pug.Sal.WebApi` and `Pug.Sal.DependencyInjection` are implementations, so
  they are `net10.0` only. `WebApi.Models` and `Client` are contracts and multi-target
  (`net6.0;net8.0;net10.0`).
- **DI** (`AddSal`):
  - It registers both facades as singletons, as Scheduling does. The host registers
    `IApplicationData<IDataSession>` and an `ISecurityManager` that resolves the principal per call.
  - `INameNormalizer` and the no-op publisher are added with `TryAdd`.
  - `AddSalDatasetImporter<T>( name )` adds a keyed singleton and records the name for the
    registry. A repeated name is an `ArgumentException`.
  - `AddSalChangePublisher<T>()` replaces the publisher, whether it is called before or after
    `AddSal`.

## D21 — CI and release pipeline (following Pug.Sqetch's)

- **All logic is in `build/build.ps1`.** The stages are `identify`, `test`, `pack`, `checksums`,
  `push` and `release`, and they run the same on a workstation. The workflows hold only triggers,
  runners, permissions and artifact handling. A composite setup action pins the SDK through
  `global.json`.
- **CI** (`ci.yml`) runs the completion gate on every branch push. `release.yml` reuses it through
  `workflow_call`.
- **Tag-driven releases:**
  - `sal-v<X.Y.Z>` builds `X.Y.Z`. `sal-pre-v<X.Y.Z>` builds `X.Y.Z-pre.<UTC stamp>`.
  - X.Y.Z must equal the single `<Version>` in `Directory.Build.props`. That replaces the old fixed
    `PackageVersion`/`AssemblyVersion`/`FileVersion`, which made `-p:Version` ineffective.
  - A tag push publishes to nuget.org: the `nuget` job, in the `release` environment. It then
    creates a draft GitHub release with the packages and `SHA256SUMS`.
  - **nuget.org trusted publishing** (user directive, 2026-10-09). No API key is stored: the
    `nuget` job's OIDC token is exchanged through `NuGet/login` for a key that lives an hour.
    nuget.org issues it only for a policy matching `NDWX/Pug.Sal`, `release.yml` and the `release`
    environment. The login user is the `NUGET_USER` variable. This replaced the `NUGET_APIKEY`
    secret the other Pug repositories use. `build.ps1 -Stage push` still takes `NUGET_API_KEY`
    from the environment, so a manual push with an ordinary key works the same.
  - `workflow_dispatch` is a dry run that publishes nothing.
- **Docker Hub pulls are authenticated in CI.** Anonymous pulls are capped per IP, and shared
  GitHub runners exhaust the cap. `ci.yml` logs in with `docker/login-action`, and Testcontainers
  then pulls `postgres:17-alpine` and Ryuk with that login. The credentials are the
  `DOCKERHUB_USERNAME` variable and the `DOCKERHUB_TOKEN` secret, which `release.yml` passes to
  `ci.yml` with `secrets: inherit`.
  - Mirrors were tried and dropped. `public.ecr.aws` worked locally but reported the image as not
    found on GitHub's runners.
- **Differences from Sqetch:**
  - Sal ships libraries, so there are no per-platform builds, installers, smoke tests or signing.
  - Sqetch defers its nuget.org push. Sal pushes, because publishing the packages is the point of a
    library release.

## D22 — Licence: AGPL (user directive, 2026-10-10)

- Every project is licensed under `AGPL-3.0-or-later`, replacing `Apache-2.0+`. The repository-root
  `LICENSE` carries the AGPL text, and `Directory.Build.props` sets `PackageLicenseExpression`
  for every package.
- `-or-later` (rather than `-only`) follows Pug.Sqetch, where the user chose it.
- The unused custom `<License>` property was removed.
- Packages already on nuget.org (`0.0.1-pre.202610100035`) keep the Apache licence they were
  published under. The next release carries the AGPL.

## D23 — Per-surface scopes and policies on the endpoints (user directive, 2026-10-14)

- **The host sets, separately for the directory (`sal/`) and administration (`sal/admin/`), OAuth
  scopes, a policy name, or both** (the user chose "both" over scopes-only and policy-only):
  `MapSalEndpoints( o => { o.Directory.Scopes = …; o.Administration.Policy = "…"; } )`.
  `MapSalDirectoryEndpoints` and `MapSalAdministrationEndpoints` take an
  `Action<SalApiAuthorization>` for the same settings. With no arguments, behaviour is unchanged.
- **Scopes:** the caller needs any one of them.
  - They are compared ordinally, as they appear in the token (for Entra, the short name in `scp`).
  - They are read from the `scope` claim (RFC 9068), `scp` (Entra) and
    `http://schemas.microsoft.com/identity/claims/scope` (`scp` after JwtBearer's inbound claim
    mapping). Each value is space-separated, and repeated claims are all read.
  - Sal checks them itself with a `RequireAssertion` policy, so the host registers nothing extra
    and Sal still references no identity provider package.
- **Policy:** the name of a policy the host registered. An unregistered name fails at request time.
- **Everything combines with AND:** the default policy (an authenticated caller), the scopes and the
  policy. An anonymous caller still gets 401. An authenticated one failing a scope or the policy
  gets 403, which every endpoint already declares.
- **An additional gate, not a replacement for D8.** The facades still authorize every operation.
- **Validated when mapping:** an empty scope, a scope containing whitespace or a blank policy name
  throws `ArgumentException`. Scopes are copied then, so changing the options later has no effect.
- **Tests:** `EndpointAuthorizationTests` runs `SalApiFixture`'s host mapped with a directory scope
  and an administration policy (`ScopedSalApiFixture`).

## D24 — Claude Code skills shipped as a versioned plugin (user directive, 2026-10-14)

- **The `pug-sal` plugin lives in this repository**, at `claude/pug-sal`. It ships the skills that
  teach an agent to extend, host and use Sal. Most users of the skills work in other repositories
  (hosts, consumers, third-party drivers), so the skills ship as a plugin rather than as this
  repository's own `.claude/skills`. Keeping the source here lets an API change and its skill
  change land in the same commit.
- **Distributed through the existing `pug-claude-plugins` marketplace** (`NDWX/claude-code-plugins`)
  as a `git-subdir` source pinned to a release tag, so the skills a user installs match the packages
  they reference.
- **The skills** (the set was revised at the user's direction):
  - `sal-development`, an overview of the extension points, and what is not one;
  - one skill per extension point: `sal-data-driver`, `sal-name-normalizer`,
    `sal-dataset-importer`, `sal-change-publisher`;
  - `sal-host`, which mentions custom components as an option;
  - `sal-directory` (reads and validation) and `sal-administration`.
- **Version lockstep (user directive).** Every bump of `<Version>` also bumps the plugin's
  `version` in the same commit, and the marketplace entry's `version` and `ref` follow the release.
  `build.ps1` (`identify`, `test`) fails when the plugin and package versions differ.
- **Known gaps the skills state rather than hide:**
  - `INameNormalizer` has no country parameter, and Sal has no re-normalization tool, so changing
    the normalizer on a populated store means re-importing every country, which loses manual
    localities.
  - D23's `Scopes` read only `scope`/`scp`. Entra app-only tokens carry `roles` instead, so a
    surface used by service callers needs a host `Policy`. `sal-host` and `sal-directory` say so.
  - The contract test suites aren't packaged, so an out-of-repo driver links their sources from a
    checkout at the matching tag.

## Verification conventions

- **Completion gate:** `dotnet build Pug.Sal.slnx -warnaserror -m:1` clean **and**
  `dotnet test tests/Pug.Sal.Tests -m:1` green.
- Tests run against real SQLite stores (a per-fixture temp DB, DbUp-migrated) with Australian
  fixture data.
- Postgres is checked with Testcontainers: `sg docker -c "dotnet test tests/Pug.Sal.Postgres.Tests -m:1"`
  runs the same contract suites against a container.

## Phase status

Each phase is committed once it passes the gate, then work **pauses for user verification**
before the next phase starts. (The repository was initialized on 2026-10-09, with phases 0–2 as the
first commit. Phases 3–6 were then built without pausing, through a misreading of the user's
instruction; the user chose to keep that work.)

- **0 Decisions:** this log, `CLAUDE.md`, `global.json`, `Directory.Build.props`. DONE.
- **1 Models + Abstractions:** contracts only, build-gated. DONE, approved 2026-10-09.
- **2 Data (SQLite).** DONE (Data.Abstractions, Data.SQLite, 28 store tests).
- **D16 amendment** (full records, administration reads, split implementations). DONE.
- **3 Core read facade + normalizer.** DONE (`LocalityServiceBase`, `LocalityDirectory`,
  `AddressValidation`, `NameNormalizer`; AU fixture tests; 86 tests in all).
- **4 Admin + import + events.** DONE (`LocalityAdministration`, `ImportCoordinator`,
  `Pug.Sal.Importers.Csv`, duplicate mapping; 135 tests in all).
- **5 Postgres.** DONE (`Pug.Sal.Data.Postgres`, `UnitsOfWork`, contract suites on both stores;
  135 SQLite + 69 Postgres tests).
- **6 WebApi + Client + DI.** DONE (`Pug.Sal.WebApi.Models`, `Pug.Sal.WebApi`, `Pug.Sal.Client`,
  `Pug.Sal.DependencyInjection`; TestServer API tests; 144 SQLite + 69 Postgres tests).
- **Skills plugin (D24), in three phases:**
  - **S1, overview and component skills.** DONE: `sal-development`, `sal-data-driver`,
    `sal-name-normalizer`, `sal-dataset-importer`, `sal-change-publisher`, and the version check.
  - **S2, `sal-host`.** DONE (with a role-to-operation `IAuthorizationProvider` reference; the
    host sample compiles against Pug.Application.Security 3.1.0).
  - **S3, `sal-directory` and `sal-administration`.** DONE. The client wiring and validation samples
    compile against Pug.Sal.Client and Pug.HttpApiClient.OAuth2 0.5.1.
  - **S2 amendment (user directive, 2026-10-14).** `sal-host` was amended for Pug.Application.Security's
    change to `OAuth20IdentityAccessor`, which is in progress. The accessor now identifies callers by
    `oid` when `tid` is present, falling back to `sub`, and returns an unauthenticated principal (not
    null) for anonymous callers. The skill also adds JwtBearer `RoleClaimType = "roles"`, and the
    reference provider refuses unauthenticated principals. **Verification is pending that release**,
    including pinning the exact package version in the skill.
  - The marketplace entry is added once S3 is done.
