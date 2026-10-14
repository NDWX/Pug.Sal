---
name: sal-data-driver
description: Use when writing a new Pug.Sal data-access driver, i.e. a store for a database Sal doesn't ship (SQL Server, MySQL, Oracle, ...) that implements IDataSession from Pug.Sal.Data.Abstractions, or when changing or reviewing the SQLite or Postgres stores. Covers the store contract, schema scripts, locking and versioning, the stored formats, transactions, and verifying against Sal's contract test suites.
---

# Writing a Pug.Sal data-access driver

A driver is a store for one database engine. It is two classes, following `Pug.Application.Data`, the same pattern as the built-in stores:

- **`DapperData : ApplicationData<IDataSession>`** takes `( string location, DbProviderFactory dataProvider )`. It creates sessions in `CreateApplicationDataSession`, and declares the schema in `InitializeUpgradeScripts()`. Hosts read the schema back through `GetSchemaUpgradeScripts()` and apply it with DbUp or similar. Sal never migrates a database itself.
- **`DapperDataSession : ApplicationDataSession, IDataSession`** holds the primitives.

Start by copying `src/Pug.Sal.Data.Postgres` from https://github.com/NDWX/Pug.Sal, at the tag that matches the `Pug.Sal.Data.Abstractions` version you reference. Copy the Postgres store rather than the SQLite one if your engine enlists in `System.Transactions` itself, as most ADO.NET providers do. The SQLite store carries a workaround (described below) that other engines must not copy.

Reference `Pug.Sal.Data.Abstractions`, `Pug.Application.Data`, Dapper (or your data-access library) and your engine's ADO.NET provider. Leave out `Pug.Sal`.

## What the store must and must not do

**The store holds no policy (DECISIONS D15).** It never authorizes, normalizes, validates, chooses timestamps or de-duplicates. The facades do all of that and compose the primitives into one transaction. A driver that "helps", for example by normalizing a name or defaulting a timestamp, breaks the stores' equivalence. The contract for each method is in [references/contract.md](references/contract.md). Implement it exactly as written there; it repeats the comments in `IDataSession.cs`, which a NuGet consumer doesn't see.

These are easy to get wrong:

1. **Locking and versioning in one call.** `AdvanceVersionAsync( country, utcNow )` must take the country row's write lock **before** it reads `currentVersion`, for example with `select … for update` (Postgres) or a no-op `update country set currentVersion = currentVersion` first (SQLite). Under that lock it writes `DatasetVersions.Next( current, utcNow )` and returns the new value. It returns null for an unknown country. Concurrent callers must be serialized and must each get a distinct version. The contract test `ConcurrentTransactionsGetDistinctVersions` checks this.
2. **Version-valued columns are text** in `DatasetVersions.TextFormat` (`yyyyMMddHHmmssfffffff`, UTC), written with `DatasetVersions.ToText` and read with `DatasetVersions.FromText`. This applies to `country.currentVersion`, `importRun.datasetVersion`, `locality.stamped` and every `retired` column (D6, D19). Don't use the engine's timestamp type: few are exact to the tick, and the text compares in time order only when its collation is bytewise (Postgres uses `collate "C"`).
3. **"None" is `''` in key columns.** `locality.subdivision` and `locality.postcodeKey` are `not null` primary-key columns that hold `''` when there's none. Map `''` back to null when reading. Codes and keys compare and sort **bytewise** (ordinal), so results come back in the same order as from the other stores.
4. **`stamped`.** Every locality row keeps the latest of its `Registration` and `LastUpdate` timestamps in a plain indexed column. Every write that touches the row must update it, as the contract says. `DeleteLocalitiesStampedBeforeAsync` is `where country = @c and stamped < @t`.
5. **Duplicate keys** throw `Pug.Sal.Data.Abstractions.DuplicateKeyException`, wrapping the engine's exception (for example Postgres `23505`, or SQLite constraint `2067`/`1555`). Other constraint failures surface as the engine's own exception.
6. **Times are UTC on the way out.** Every `DateTime` returned has `DateTimeKind.Utc`. `ActionContext<Reference>` columns (`registration`, `lastUpdate`, `performed`) are JSON text. Use a Dapper type handler, like the stores' `JsonTypeHandler<T>`.
7. **Search.** `SearchLocalitiesAsync` takes a name prefix **literally**: `%`, `_` and `\` in it match only themselves, so use `left( nameKey, n ) = @prefix` rather than an unescaped `LIKE`. It must use the `(country, nameKey)` index. It may add fuzzy matches after the prefix matches, as Postgres does with `pg_trgm`. Prefix matches come first, and the result holds at most `limit` rows.

## Schema

Keep the **same tables and columns** as the built-in stores: `country`, `subdivision`, `locality`, `importRun`, with their primary keys and the indexes `(country, nameKey)`, `(country, postcodeKey)`, `(country, stamped)` and `importRun(country, datasetVersion desc)`. The contract suites read `country.currentVersion` and `locality.stamped` directly with SQL, and keeping the same names lets the stores be compared. Version the scripts the way the stores do (`SchemaVersion( 1, … )`, each `UpgradeScript` with a revert). Never edit a released script: add a new `SchemaVersion`.

## Transactions

The facades run each unit of work through `UnitsOfWork.ReadAsync` / `WriteAsync` (D19). Each opens its own `TransactionScope` (`RequiresNew`): writes at ReadCommitted and reads at RepeatableRead. Your session must make every statement of a unit of work part of that scope.

- **The engine's provider enlists in `System.Transactions` itself** (Npgsql, Microsoft.Data.SqlClient, MySqlConnector): do nothing extra. Check it with a rollback test.
- **The provider ignores ambient transactions** (Microsoft.Data.Sqlite): open a real transaction and tie it to the scope with an `EnlistVolatile` enlistment that commits in phase one. This is the SQLite store's `ScopeEnlistment`. Without it, statements autocommit, there's no atomicity, and `AdvanceVersionAsync` holds no lock.
- **The engine fails a writer that waited on a lock** ("could not serialize") at the write isolation level: don't raise the level. Writers serialize on the country lock, and ReadCommitted is what lets the second writer proceed.

## Verifying

The driver is correct when it passes Sal's two contract suites, unchanged:

- `StoreContractTests<TFixture>`: the `IDataSession` primitives (25 tests).
- `LocalityAdministrationContractTests<TFixture>`: the administration facade over your store, imports included (18 tests).

They live in `tests/Pug.Sal.Tests`, which isn't published as a package. Run them the way `tests/Pug.Sal.Postgres.Tests` does:

1. Create an xUnit test project that links the shared sources from a Pug.Sal checkout at the matching tag: `StoreContractTests.cs`, `LocalityAdministrationContractTests.cs`, `Infrastructure/IStoreFixture.cs`, `Infrastructure/SecurityManagers.cs` and `Infrastructure/RecordingEventPublisher.cs`. Use `<Compile Include="…" Link="Shared/…" />`, or copy them and record the tag you copied from. They need `Moq`, `Dapper`, and project or package references to `Pug.Sal`, `Pug.Sal.Importers.Csv` and your driver.
2. Write an `IStoreFixture` that starts an empty database (a Testcontainers container, or a per-fixture database), applies `GetSchemaUpgradeScripts()` in order, and exposes `ApplicationData` and `QueryTextAsync( sql, parameters )`. `PostgresDatabaseFixture.cs` is the template.
3. Declare `public sealed class MyStoreTests : StoreContractTests<MyDatabaseFixture>` and `MyLocalityAdministrationTests : LocalityAdministrationContractTests<MyDatabaseFixture>`, each with a constructor that takes the fixture.
4. Run them with `dotnet test -m:1`. Every test must pass. A failing test means the store differs from the others. Fix the store, never the test.

In the Pug.Sal repository itself, a new store is a new `src/Pug.Sal.Data.<Engine>` project plus a `tests/Pug.Sal.<Engine>.Tests` project, added to `build/build.ps1`'s test stage. All the stores are then kept in step: a schema or behaviour change lands in every store in the same commit.

## Host wiring

The host registers the store and migrates it. See `sal-host`.

```csharp
services.AddSingleton<IApplicationData<IDataSession>>( new MyEngine.DapperData( connectionString, MyProviderFactory.Instance ) );
services.AddSal();
```
