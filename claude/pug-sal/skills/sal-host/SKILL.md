---
name: sal-host
description: Use when hosting Pug.Sal (the address reference-data service) in an application, i.e. wiring AddSal, a SQLite or Postgres store and its schema migration, ISecurityManager and permissions, the sal/ and sal/admin/ HTTP endpoints with per-surface OAuth scopes and policies, OpenAPI, dataset importers and change publishing, or embedding Sal in-process without HTTP. Also covers when a host needs custom components.
---

# Hosting Pug.Sal

Sal is a library. A host is the application that embeds it: an ASP.NET Core service exposing the HTTP API, or any .NET application calling the facades in-process. The host supplies four things Sal never chooses for itself:

1. a **store**, migrated;
2. an **`ISecurityManager`**, which answers "who is calling, and may they do this";
3. **authentication** for the HTTP surface;
4. the **importers** and the **change publisher** it wants.

## Packages

| Package | For |
|---|---|
| `Pug.Sal.DependencyInjection` | `AddSal`, `AddSalDatasetImporter<T>`, `AddSalChangePublisher<T>`. Brings in `Pug.Sal` |
| `Pug.Sal.Data.Postgres` or `Pug.Sal.Data.SQLite` | The store (`DapperData`). For a different engine, see `sal-data-driver` |
| `Pug.Sal.WebApi` | `MapSalEndpoints` and friends. Skip it for in-process-only use |
| `Pug.Sal.Importers.Csv` | The CSV importer, if the host imports datasets |
| The engine's ADO.NET provider | `Npgsql`, or `Microsoft.Data.Sqlite` (the full package: the store references only `.Core`, and the host supplies the native bundle) |
| `Pug.Application.Security` + `Pug.Application.Security.PrincipalIdentityAccessors.AspNetCore`, **3.1.x** | `SecurityManager` and the OAuth 2.0 identity accessor, unless you have your own `ISecurityManager`. Pin 3.1.x, to match the `Pug.Application.Security.Abstractions` 3.1.0 that Sal references. nuget.org also lists a date-versioned `2026.4.x` line, which NuGet ranks as newer but which is a different line |
| `dbup-postgresql` / `dbup-sqlite` | Applying the schema (any migration tool works) |

Every `Pug.Sal.*` package must be at **the same version**. They reference each other exactly.

## A complete ASP.NET Core host (Postgres, Entra ID)

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder( args );
string connectionString = builder.Configuration.GetConnectionString( "Sal" )!;

// 1. Store
Pug.Sal.Data.Postgres.DapperData store = new( connectionString, NpgsqlFactory.Instance );
builder.Services.AddSingleton<IApplicationData<IDataSession>>( store );

// 2. Security: who is calling (from the request's token), and what they may do
builder.Services.AddHttpContextAccessor();
builder.Services.AddOAuth20IdentityAccessor();   // Pug.Application.Security.PrincipalIdentityAccessors.AspNetCore: IPrincipalIdentityAccessor over HttpContext.User
builder.Services.AddAzureADRoleProvider( tenantId, clientId, clientSecret );   // Pug.Application.Security.AzureADRoleProvider.DependencyInjection: IPrincipalRoleProvider from Entra app roles
builder.Services.AddSingleton<IAuthorizationProvider, SalRoleAuthorization>();   // yours, see "Permissions"
builder.Services.AddSingleton<ISecurityManager>( p => new SecurityManager(
	"Sal", p.GetRequiredService<IPrincipalIdentityAccessor>(), p.GetService<IPrincipalRoleProvider>(), p.GetRequiredService<IAuthorizationProvider>() ) );

// 3. Authentication, plus the host-defined admin policy used below
builder.Services.AddAuthentication( JwtBearerDefaults.AuthenticationScheme )
	.AddJwtBearer( o => { builder.Configuration.Bind( "Jwt", o ); o.MapInboundClaims = false; } );
builder.Services.AddAuthorization( o => o.AddPolicy( "SalAdmin", p => p.RequireRole( "Sal.Admin" ) ) );

// 4. Sal, its importers and its publisher
builder.Services.AddSal();
builder.Services.AddSalDatasetImporter<CsvDatasetImporter>( CsvDatasetImporter.DefaultName );
// builder.Services.AddSalChangePublisher<MyBrokerPublisher>();   // optional; see sal-change-publisher

WebApplication app = builder.Build();

MigrateSalSchema( connectionString, store );

app.UseAuthentication();
app.UseAuthorization();

app.MapSalEndpoints( o =>
{
	o.Directory.Scopes = new[] { "Sal.Read" };
	o.Administration.Scopes = new[] { "Sal.Admin" };
	o.Administration.Policy = "SalAdmin";
} );

app.Run();
```

The rest of this skill explains each piece.

## Store and schema

- **Register the store** as a singleton `IApplicationData<IDataSession>`: `new DapperData( connectionString, NpgsqlFactory.Instance )`, or `new Pug.Sal.Data.SQLite.DapperData( connectionString, SqliteFactory.Instance )`.
- **Sal never migrates.** The store exposes its scripts through `GetSchemaUpgradeScripts()`, in order, and the host applies them at startup or in its deployment pipeline. With DbUp:

  ```csharp
  static void MigrateSalSchema( string connectionString, ApplicationData<IDataSession> store )
  {
  	UpgradeEngineBuilder builder = DeployChanges.To.PostgresqlDatabase( connectionString );   // SQLiteDatabase for SQLite

  	foreach( SchemaUpgradeScript s in store.GetSchemaUpgradeScripts() )
  		builder = builder.WithScript( s.UpgradeScript.Name, s.UpgradeScript.Script );

  	DatabaseUpgradeResult result = builder.LogToConsole().Build().PerformUpgrade();

  	if( !result.Successful )
  		throw new InvalidOperationException( "Sal schema migration failed.", result.Error );
  }
  ```

  If you share the database with other schemas, give Sal its own database or schema, because its table names are plain (`country`, `locality`, ...).
- **Postgres:** the schema runs `create extension if not exists pg_trgm` (fuzzy search). The migrating role must be allowed to create it: `pg_trgm` is trusted, so the database owner can. You can tune how fuzzy search is with `pg_trgm.similarity_threshold`, set per database or role.
- **SQLite:** reference the full `Microsoft.Data.Sqlite` package for the native library. Enable WAL (`PRAGMA journal_mode=WAL`) and set a `busy_timeout`, because writers serialize on the database lock. SQLite suits single-instance hosts. Use Postgres when several instances share a store.

## Security: `ISecurityManager` and permissions

**Every operation is authorized, reads included (DECISIONS D8).** Each facade call asks `ISecurityManager.CurrentPrincipal.IsAuthorizedAsync( context, operation, resource, purpose )`. If that's false, the call throws `NotAuthorized`, which becomes 403 over HTTP. **If `CurrentPrincipal` is null, the call is refused, never trusted.**

- **The facades are singletons**, so `CurrentPrincipal` must resolve the *current* caller each time it is read. `SecurityManager` does this through `IPrincipalIdentityAccessor`. `AddOAuth20IdentityAccessor()` reads `HttpContext.User`, and needs `AddHttpContextAccessor()`.
- **The actor recorded on every write** (`Registration`, `LastUpdate`, `ImportRun.Performed`, events) is `CurrentPrincipal.Identity.Identifier`. The OAuth accessor takes it from the token's `sub` claim, or `client_id` for app-only tokens. **Set `MapInboundClaims = false` on JwtBearer.** Otherwise `sub` is renamed, and writes are recorded with an empty actor.
- **Work outside a request**, such as a scheduled import or a seeding job, has no `HttpContext`, and so no principal, and is refused. Run it under an explicit identity: give it an `ISecurityManager` whose `CurrentPrincipal` is a service principal holding just the operations it needs (`IMPORT`, `VIEW`). Don't make your request-path security manager fall back to "system".

**Permissions.** Sal checks these operations (`Pug.Sal.Abstractions.SecurityOperations`), always against `ResourceQualifiers.AllCountries` (`{ Type = "COUNTRY" }`). There are no per-country grants for now (D8).

| Operation | Needed for |
|---|---|
| `VIEW` | Every directory call, including validation, and the administration reads |
| `CREATE` | Creating countries, subdivisions and localities |
| `MODIFY` | Updating countries and subdivisions (localities are never updated) |
| `RETIRE` | Retiring and reinstating anything |
| `DELETE` | Deleting localities |
| `IMPORT` | Imports and import history |

Decide them in your `IAuthorizationProvider`. A typical role mapping, for example from Entra app roles through `Pug.Application.Security.AzureADRoleProvider` registered as the `IPrincipalRoleProvider`, is:

- `Sal.Read` gets `VIEW`;
- `Sal.Maintain` gets every operation except `IMPORT`;
- `Sal.Admin` gets everything.

Derive from `RoleBasedAuthorizationProvider` and implement `PrincipalIsAuthorized[Async]` with that table. [references/SalRoleAuthorization.cs](references/SalRoleAuthorization.cs) is a complete one. It refuses any resource that isn't `COUNTRY`, so a new resource type Sal adds later is denied until you grant it. Sal never references an identity provider: this mapping is entirely the host's.

## HTTP endpoints

- `app.MapSalEndpoints()` maps the directory under `sal/` and administration under `sal/admin/` (D20).
  - **Read-only host:** `app.MapSalDirectoryEndpoints()` alone.
  - **Under a prefix:** map onto a group, `app.MapGroup( "/api" ).MapSalEndpoints()`.
  - **Separate deployments:** map the two surfaces in different hosts over the same store.
- **Every endpoint requires an authenticated caller**: anonymous gets 401. On top of that, the options can require, **per surface**, any one of a list of OAuth scopes, and/or a host-registered policy (D23). The requirements combine with AND, and a failure is 403. Scopes are matched ordinally as they appear in the token, so for Entra use the short name in `scp`, not the `api://…/` URI. They are read from `scope`, `scp` and the mapped Entra scope claim. An unregistered policy name fails at request time, and invalid scopes throw when mapping.
- **These are an extra gate, not the authorization.** The facades still check `ISecurityManager` on every call. Keep both consistent: a scope that admits a caller the provider refuses just moves the 403 later.
- **OpenAPI.** Every endpoint carries a name, a tag ("Sal" or "Sal administration"), a summary and its exact response statuses. Sal serves no document itself. The host enables one, typically in development only, with `AddEndpointsApiExplorer()` + `AddSwaggerGen()`, or `AddOpenApi()` + `MapOpenApi()`.
- **Imports over HTTP** post the dataset as the raw body (`application/octet-stream`, `text/csv` or `text/plain`; anything else gets 415). Kestrel's default request-body limit is about 30 MB. Raise it for the import route if your datasets are larger (`MaxRequestBodySize`, or `[RequestSizeLimit]` metadata through `WithMetadata` on a group). Sal holds a whole parsed dataset in memory during an import.
- **Caching.** Reads return ETags, and `If-None-Match` gets 304. Put no response cache in front that ignores the `ETag`/version semantics. Clients cache per country by dataset version.

## In-process, without HTTP

Skip `Pug.Sal.WebApi`, and inject the facades:

```csharp
public sealed class CheckoutAddressValidator( ILocalityDirectory directory ) { /* await directory.ValidateAsync( candidate ) */ }
```

The same security rules apply. The calling code must run where `ISecurityManager.CurrentPrincipal` resolves the user. A host that calls Sal in-process for its own users usually uses the same accessor as its other APIs. For calling Sal, see `sal-directory` and `sal-administration`.

## Importers and change events

- Register each importer under the name callers will use: `AddSalDatasetImporter<CsvDatasetImporter>( "csv" )`. The registry starts empty, and an import naming an unregistered importer gets 422 (`UnknownDatasetImporterException`). Whether a dataset may be loaded under its licence is the host's decision. Sal has no licence model.
- `AddSalChangePublisher<T>()` replaces the no-op publisher, in either order relative to `AddSal`. Publishing is after commit, best-effort, and awaited on the request path (D7).

## Custom components, when the built-ins don't fit

A host may need to supply its own component at one of Sal's four extension points:

- **Data-access driver:** a database other than Postgres or SQLite. See `sal-data-driver`.
- **Dataset importer:** a dataset format other than column-mapped CSV. See `sal-dataset-importer`.
- **Change event publisher:** sending changes to a broker, an audit log or a cache. See `sal-change-publisher`.
- **Name normalizer:** different matching rules for names or postcodes. Decide this **before** loading any data. See `sal-name-normalizer`.

`sal-development` gives the overview and the rules every component keeps. Authorization is not among them: it is the `ISecurityManager` and `IAuthorizationProvider` above.

## Loading the first data

A new store is empty. Sal ships no reference data, and an import never creates countries or subdivisions. As a principal holding `CREATE` and `IMPORT`:

1. create each country (`CountryDefinition`: code, name, postcode pattern and whether one is required, and whether it has subdivisions);
2. create its subdivisions;
3. import its dataset.

Details are in `sal-administration`.

## Checklist

- [ ] All `Pug.Sal.*` packages are at one version.
- [ ] The store is registered as a singleton, and the schema is migrated before the first request.
- [ ] `ISecurityManager` resolves the current caller per call. The actor identifier is non-empty on writes (check `Registration.Actor` after a test write). Background jobs run under an explicit principal.
- [ ] The `IAuthorizationProvider` maps every `SecurityOperations` value deliberately, `DELETE` and `IMPORT` included.
- [ ] Endpoint scopes and policies match the provider's grants.
- [ ] Every importer callers will use is registered, and the request size limit fits your datasets.
- [ ] A smoke test passes: an anonymous `GET sal/countries` gets 401, a reader gets 200, and a reader posting to `sal/admin/countries` gets 403.
