---
name: sal-host
description: Use when hosting Pug.Sal (the address reference-data service) in an application, i.e. wiring AddSal, a SQLite or Postgres store and its schema migration, ISecurityManager and permissions, the directory and administration HTTP endpoints with their route prefixes and per-surface OAuth scopes and policies, OpenAPI, dataset importers and change publishing, or embedding Sal in-process without HTTP. Also covers when a host needs custom components.
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
| `Pug.Application.Security` **≥ 3.1.1** + `Pug.Application.Security.PrincipalIdentityAccessors.AspNetCore` **≥ 3.2.0** | `SecurityManager` and the OAuth 2.0 identity accessor, unless you have your own `ISecurityManager`. Accessor 3.2.0 identifies Entra callers by `oid` and returns an anonymous identity for unauthenticated requests. `Principal` 3.1.1 never asks the role provider about an anonymous identity. Earlier versions identify Entra users by `sub`, which the Entra role provider can't resolve (see "Security"). Sal itself references `Pug.Application.Security.Abstractions` 3.1.1. **Pin exact versions** (`3.1.1`, `3.2.0`), never a floating or "latest" one: nuget.org also lists a date-versioned `2026.4.x` line for Security and Abstractions, which NuGet ranks above 3.x but which is a different line |
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
	.AddJwtBearer( o =>
	{
		builder.Configuration.Bind( "Jwt", o );
		o.MapInboundClaims = false;                          // keep Entra's claim names: oid, tid, sub, scp, roles
		o.TokenValidationParameters.RoleClaimType = "roles"; // so RequireRole reads Entra app roles
	} );
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
	// Mounted at directory/ and administration/ unless you set RoutePrefix.
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
- **SQLite:** reference the full `Microsoft.Data.Sqlite` package for the native library. Enable WAL (`PRAGMA journal_mode=WAL`) and set a `busy_timeout`, because writers serialize on the database lock. Reads take no write lock: in WAL mode they neither block writers nor each other. Without WAL, a read still delays a writer's commit until it ends. SQLite suits single-instance hosts. Use Postgres when several instances share a store.

## Security: `ISecurityManager` and permissions

**Every operation is authorized, reads included (DECISIONS D8).** Each facade call asks `ISecurityManager.CurrentPrincipal.IsAuthorizedAsync( context, operation, resource, purpose )`. If that's false, the call throws `Pug.Sal.Abstractions.AccessDeniedException`, which becomes 403 over HTTP. **A call whose `CurrentPrincipal` is null, or whose identity isn't authenticated (`Identity.IsAuthenticated` false), is refused with `AccessDeniedException` before your authorization provider is asked.**

- **The facades are singletons**, so `CurrentPrincipal` must resolve the *current* caller each time it is read. `SecurityManager` does this through `IPrincipalIdentityAccessor`. `AddOAuth20IdentityAccessor()` reads `HttpContext.User`, and needs `AddHttpContextAccessor()`.
- **The caller's identifier** is `CurrentPrincipal.Identity.Identifier`. Sal records it as the actor on every write (`Registration`, `LastUpdate`, `ImportRun.Performed`, events), and the role provider looks the caller up by it.
  - **What the accessor uses:** when the token carries both a tenant ID (`tid`) and an object ID (`oid`), as Entra tokens do, the OAuth accessor treats it as an Entra token and uses the caller's **object ID**. It types the caller as an application when `oid` equals `sub` (an app-only token). Any other token is identified by `sub`, or by `client_id` when it has no subject of its own.
  - **Why it matters for Entra:** the object ID is what `AzureADRoleProvider` resolves in Graph, and it is the same across every application. Entra's `sub` is a per-application pairwise value, so Graph can't resolve it, and a user identified by it would get no roles.
  - **Claim names:** see "JwtBearer claim mapping and roles" below.
- **Unauthenticated callers get a principal, not null.** For a request without a valid token, or with no usable identifier, the accessor returns an anonymous identity (`IsAuthenticated` false, no identifier). It returns null only when there is no HTTP request at all.
  - **Over HTTP:** the endpoints refuse such a caller with 401 before Sal is reached.
  - **In-process:** Sal refuses it with `AccessDeniedException` without consulting your provider.
  - **In your provider:** `Pug.Application.Security`'s `Principal` passes anonymous identities to the `IAuthorizationProvider`, which other code than Sal may call. So refuse unauthenticated principals there too. [The reference provider](references/SalRoleAuthorization.cs) checks `IsAuthenticated` first.
- **Work outside a request**, such as a scheduled import or a seeding job, has no `HttpContext`, and so no authenticated caller, and is refused. Run it under an explicit identity: give it an `ISecurityManager` whose `CurrentPrincipal` is a service principal holding just the operations it needs (`IMPORT`, `VIEW`). Don't make your request-path security manager fall back to "system".

### JwtBearer claim mapping and roles

JwtBearer's `MapInboundClaims` decides the names token claims arrive under, and that decides whether role-based policies work. Pick one setting and configure to match it:

| `MapInboundClaims` | Claims arrive as | `RequireRole( "Sal.Admin" )` | Do this |
|---|---|---|---|
| `false` (the sample) | Their token names: `oid`, `tid`, `sub`, `scp`, `roles` | **Fails for everyone (403)** unless `TokenValidationParameters.RoleClaimType = "roles"` | Set `RoleClaimType = "roles"` |
| `true` (the ASP.NET default) | Long URIs: `roles` becomes `ClaimTypes.Role`, `sub` becomes `ClaimTypes.NameIdentifier`, `oid`/`tid`/`scp` become `http://schemas.microsoft.com/identity/claims/…` | Works as is | Nothing extra. Write any claim-based checks of your own against the long names |

The two Sal-side readers work either way. The OAuth accessor reads both the short and the long names of `sub`, `oid` and `tid`, and D23's scope check reads `scope`, `scp` and the long scope name. The setting only affects your own policies and claim checks. The trap is turning mapping off and forgetting `RoleClaimType`: authentication still succeeds, but every role-gated endpoint answers 403. This was verified with signed tokens through JwtBearer: 403 with mapping off and no `RoleClaimType`, 200 with it set, and 200 with mapping on.

**Permissions.** Sal checks these operations (`Pug.Sal.Abstractions.SecurityOperations`) against `ResourceQualifiers.AllCountries` (`{ Type = "COUNTRY" }`), except for the global locality category list, which is checked against `ResourceQualifiers.AllCategories` (`{ Type = "CATEGORY" }`, D28) so that its upkeep can be granted separately. There are no per-country grants for now (D8).

| Operation | Needed for |
|---|---|
| `VIEW` | Every directory call, including validation, and the administration reads. On `CATEGORY`: reading the global category list |
| `CREATE` | Creating countries, subdivisions and localities. On `CATEGORY`: creating global categories |
| `MODIFY` | Updating countries and subdivisions (localities are never updated), and assigning or unassigning a country's categories. On `CATEGORY`: renaming global categories |
| `RETIRE` | Retiring and reinstating anything, and disabling (with replacement) or enabling a country's categories. On `CATEGORY`: retiring and reinstating global categories |
| `DELETE` | Deleting localities |
| `IMPORT` | Imports and import history |

Decide them in your `IAuthorizationProvider`. A typical role mapping, for example from Entra app roles through `Pug.Application.Security.AzureADRoleProvider` registered as the `IPrincipalRoleProvider`, is:

- `Sal.Read` gets `VIEW`, on both resources;
- `Sal.Maintain` gets every operation except `IMPORT` on `COUNTRY`, and `VIEW` on `CATEGORY`;
- `Sal.Admin` gets everything, so it alone maintains the global category list.

Derive from `RoleBasedAuthorizationProvider` and implement `PrincipalIsAuthorized[Async]` with that table. [references/SalRoleAuthorization.cs](references/SalRoleAuthorization.cs) is a complete one. It grants per resource type and refuses any other, so a new resource type Sal adds later is denied until you grant it. Sal never references an identity provider: this mapping is entirely the host's.

## HTTP endpoints

- `app.MapSalEndpoints()` maps the directory under `directory/` and administration under `administration/` (D20).
  - **Choosing the prefixes:** set each surface's `RoutePrefix`, for example `o.Directory.RoutePrefix = "sal"` and `o.Administration.RoutePrefix = "sal/admin"`. Leading and trailing slashes are ignored. An empty prefix mounts the surface on the builder itself. A prefix with an empty segment (`a//b`) throws when mapping.
  - **Under a further prefix:** map onto a group, `app.MapGroup( "/api" ).MapSalEndpoints()`, which gives `api/directory/…`.
  - **Clients must be told the same prefix.** `LocalityDirectoryClient` and `LocalityAdministrationClient` take it as their second argument, relative to the client's base address. They default to `directory` and `administration` too. A mismatch shows up as 404s.
  - **`Location` headers** of created records are generated by routing, so they include every prefix and the request's `PathBase`.
  - **Read-only host:** `app.MapSalDirectoryEndpoints()` alone.
  - **Separate deployments:** map the two surfaces in different hosts over the same store.
- **Every endpoint requires an authenticated caller**: anonymous gets 401. On top of that, the options can require, **per surface**, any one of a list of OAuth scopes, and/or a host-registered policy (D23). The requirements combine with AND, and a failure is 403. Scopes are matched ordinally as they appear in the token, so for Entra use the short name in `scp`, not the `api://…/` URI. They are read from `scope`, `scp` and the mapped Entra scope claim. An unregistered policy name fails at request time, and invalid scopes throw when mapping.
- **Scopes only fit delegated (user) tokens.** Entra app-only tokens (client credentials, for service callers such as a backend validating addresses) carry `roles`, not `scp`, so a `Scopes` requirement refuses them with 403. If service callers use a surface, require a **policy** there instead, for example `RequireAssertion` accepting the scope *or* an app role. Use `Scopes` alone only where every caller is a user.
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

A new store is empty. Sal ships no reference data, and an import never creates countries, subdivisions or categories. As a principal holding `CREATE` and `IMPORT` (and `CREATE` on `CATEGORY`):

1. create the global locality categories (for example `SUBURB`, `TOWN`);
2. create each country (`CountryDefinition`: code, name, postcode pattern and whether one is required, whether it has subdivisions, and its first categories);
3. create its subdivisions;
4. import its dataset, every row with a category (a column, or the CSV importer's `defaultCategory` option).

Details are in `sal-administration`.

## Checklist

- [ ] All `Pug.Sal.*` packages are at one version.
- [ ] The store is registered as a singleton, and the schema is migrated before the first request.
- [ ] `ISecurityManager` resolves the current caller per call. The actor identifier is the caller's Entra object ID (check `Registration.Actor` after a test write). Background jobs run under an explicit principal.
- [ ] JwtBearer's claim mapping matches your policies (with `MapInboundClaims = false`, `RoleClaimType = "roles"` is set), and a role-gated endpoint admits a caller who holds the role.
- [ ] The `IAuthorizationProvider` refuses an unauthenticated principal.
- [ ] The `IAuthorizationProvider` maps every `SecurityOperations` value deliberately, `DELETE` and `IMPORT` included, on both the `COUNTRY` and `CATEGORY` resources.
- [ ] Endpoint scopes and policies match the provider's grants.
- [ ] Every importer callers will use is registered, and the request size limit fits your datasets.
- [ ] Clients are given the route prefixes the host maps (or both sides use the defaults).
- [ ] A smoke test passes: an anonymous `GET directory/countries` gets 401, a reader gets 200, and a reader posting to `administration/countries` gets 403 (with your prefixes, if you changed them).
