---
name: sal-development
description: Use when extending or customising Pug.Sal (the address reference-data service) with your own component, or when deciding whether Sal's built-in components fit. It maps Sal's four extension points (data-access driver, name normalizer, dataset importer, change event publisher), where each plugs in, the rules each must keep, and what is deliberately not an extension point. Sends you to the matching component skill.
---

# Developing custom components for Pug.Sal

Sal is a library a host application embeds. Its behaviour is fixed, except at four extension points. Each one is an interface in a contract package, which the host registers in DI. Before writing one, check that the built-in component really doesn't fit: each of them is tested against the whole contract.

## The extension points

| Component | Interface (package) | Built-in | Registered with | Skill |
|---|---|---|---|---|
| Data-access driver (store) | `IDataSession`, behind `IApplicationData<IDataSession>` (`Pug.Sal.Data.Abstractions`) | `Pug.Sal.Data.SQLite`, `Pug.Sal.Data.Postgres` | `services.AddSingleton<IApplicationData<IDataSession>>( new DapperData( connectionString, factory ) )` | `sal-data-driver` |
| Name normalizer | `INameNormalizer` (`Pug.Sal.Abstractions`) | `NameNormalizer` (`Pug.Sal`) | `services.AddSingleton<INameNormalizer, T>()` | `sal-name-normalizer` |
| Dataset importer | `IDatasetImporter` (`Pug.Sal.Abstractions`) | `CsvDatasetImporter` (`Pug.Sal.Importers.Csv`) | `services.AddSalDatasetImporter<T>( "name" )` | `sal-dataset-importer` |
| Change event publisher | `ILocalityChangeEventPublisher` (`Pug.Sal.Abstractions`) | a no-op | `services.AddSalChangePublisher<T>()` | `sal-change-publisher` |

The registration methods are in `Pug.Sal.DependencyInjection`. `AddSal()` registers the two facades, `ILocalityDirectory` and `ILocalityAdministration`, as singletons. It also registers the defaults, using `TryAdd` for the normalizer and the publisher. A component should depend only on the contract packages (`Pug.Sal.Models`, `Pug.Sal.Abstractions`, `Pug.Sal.Data.Abstractions`), never on `Pug.Sal` itself.

## Rules every component keeps

- **It is a singleton.** The facades are singletons and capture their components when constructed. A component must be thread-safe, and it must not hold per-request state. If it needs a scoped service, it takes an `IServiceProvider` or a factory and creates a scope each time it is called.
- **It is not where authorization happens.** The facades authorize every operation before calling a component (DECISIONS D8). A component never checks permissions, and it never bypasses the facades to write to the store.
- **Times are UTC.** Every `DateTime` Sal passes or expects is UTC, with `DateTimeKind.Utc`.
- **Library code awaits with `.ConfigureAwait( false )`.**
- **Its target frameworks match its contracts.** Contracts target `net6.0;net8.0;net10.0`, and Sal's implementations target `net10.0`. A driver or importer may target `net10.0` only.

## Not extension points

The skills above cover everything that is replaceable. These are fixed on purpose:

- **Authorization.** Sal calls `ISecurityManager.CurrentPrincipal.IsAuthorizedAsync` (from `Pug.Application.Security`) and never references an identity provider. Whether a user holds a Sal permission, for example through Entra roles, is decided by the host's security manager and its provider. The HTTP endpoints add optional per-surface OAuth scopes and policies (D23). Both are covered in `sal-host`. Do not write an "authorization component" for Sal.
- **Validation, matching and import semantics.** These live in the facades (`LocalityDirectory`, `LocalityAdministration`, the internal `ImportCoordinator`). They cover: validation never auto-corrects; imports make a country match its dataset (D13); and every write advances the country's version exactly once. A component cannot change them, and must not try to work around them, for example by writing directly to the store.
- **The data model.** Countries, subdivisions and localities are a fixed three-level model (D2), and a locality's identity is `(country, subdivision, normalized postcode, normalized name)`. There is no custom model or extra field.
- **Licensing of datasets.** Whether a dataset may be loaded is the host's business, outside Sal.

## Where to go next

- Writing a component: open the skill in the table.
- Wiring components into an application: `sal-host`.
- Calling Sal: `sal-directory` (reads and address validation) or `sal-administration` (maintenance and imports).

The design reasons are in `docs/DECISIONS.md` in the Pug.Sal repository (https://github.com/NDWX/Pug.Sal). The D-numbers cited in these skills refer to it.
