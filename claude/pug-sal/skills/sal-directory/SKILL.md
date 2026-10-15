---
name: sal-directory
description: Use when an application reads from Pug.Sal or validates addresses against it, i.e. calling ILocalityDirectory in-process or over HTTP with LocalityDirectoryClient, interpreting ValidationResult (outcomes, Match, Suggestions), building suburb/postcode autocomplete, listing countries, subdivisions and localities, or caching reference data by dataset version.
---

# Reading from Pug.Sal and validating addresses

`ILocalityDirectory` (`Pug.Sal.Abstractions`) is Sal's read API: lookup, listing, search and validation over countries, subdivisions (states, territories) and localities (suburb/postcode pairs). It has two implementations with **the same contract, exceptions included**:

- **in-process**, `LocalityDirectory`, when your application hosts Sal (`AddSal()`; see `sal-host`);
- **over HTTP**, `LocalityDirectoryClient` (`Pug.Sal.Client`), against a host that maps the directory endpoints (under `directory/` unless the host chose another prefix).

Code against the interface, and choose the implementation in DI. Results are the audit-free `CountryInfo`, `SubdivisionInfo` and `LocalityInfo`. Writes and audit fields are in `sal-administration`.

## Calling Sal over HTTP

Reference `Pug.Sal.Client` at the same version as the server. The client runs on `net6.0`, `net8.0` and `net10.0`. It sits on `Pug.HttpApiClient`, and credentials are added by a request decorator:

```csharp
services.AddHttpClient();
services.AddSingleton<IHttpApiClient>( p =>
{
	IHttpClientFactory http = p.GetRequiredService<IHttpClientFactory>();

	return new HttpApiClient( new Uri( "https://sal.example.com/" ), http,   // the host, with any base path it is served under; keep the trailing slash
		messageDecorators: new IHttpRequestMessageDecorator[]
		{
			// Service-to-service (Pug.HttpApiClient.OAuth2). For calls on behalf of a signed-in user,
			// forward their token instead (TokenExchangeCredentialsDecorator, or an Authorization header).
			new ClientCredentialsDecorator( tokenEndpoint, clientId, clientSecret, "api://sal/.default", http )
		} );
} );
// The second argument is the host's route prefix for the directory; omit it for the default, "directory".
services.AddSingleton<ILocalityDirectory>( p => new LocalityDirectoryClient( p.GetRequiredService<IHttpApiClient>(), "directory" ) );
```

- **Route prefix.** The client must use the prefix the host mapped the directory under (`SalApiOptions.RoutePrefix`, default `directory`). It is relative to the base address, so `new Uri( "https://example.com/api/" )` with `"sal"` calls `https://example.com/api/sal/countries`. Keep a trailing slash on a base address that has a path. A wrong prefix shows up as 404.

- **Permissions.** Every call, reads included, needs the `VIEW` permission for the caller on the server. Which roles grant it is the host's choice.
- **Scopes.** If the host requires OAuth scopes on the directory (D23), the token must carry one of them in `scp`/`scope`. **Entra app-only tokens (client credentials) carry `roles`, not `scp`**, so a service caller is refused (403) by a scope requirement. Agree with the host on a policy based on app roles for service callers.

## Operations

| Call | Returns | Notes |
|---|---|---|
| `GetCountriesAsync( includeRetired )` | `CountryInfo[]` | Each carries `CurrentVersion` |
| `GetCountryVersionsAsync()` | `CountryVersion[]` | Every country's current version, retired ones included. The cheap read to revalidate a cache with |
| `GetCountryAsync( country )` | `CountryInfo` | Its `Definition` holds `PostcodePattern`, `PostcodeRequired` and `HasSubdivisions`, which are useful for form rules |
| `GetSubdivisionsAsync( country, includeRetired )` | `Versioned<SubdivisionInfo[]>` | By code |
| `GetSubdivisionAsync( country, code )` | `Versioned<SubdivisionInfo>` | Matched by **code** only |
| `GetLocalitiesAsync( country, subdivision?, postcode?, name?, includeRetired )` | `Versioned<LocalityInfo[]>` | Any combination of filters. Name and postcode are matched on normalized forms (`"st kilda"` finds `St Kilda`). With no filter, it returns the whole country |
| `GetLocalityAsync( country, subdivision, postcode, name )` | `Versioned<LocalityInfo>` | By full key. Pass null for an absent subdivision or postcode |
| `SearchLocalitiesAsync( country, text, subdivision?, limit = 20 )` | `Versioned<LocalityInfo[]>` | Autocomplete: current localities whose normalized name **starts with** the text, best first. Postgres stores also append close fuzzy matches. `limit` must be ≥ 1 and is capped at 100. Empty text is an `ArgumentException` |
| `ValidateAsync( AddressCandidate )` | `ValidationResult` | See below |

**Input forms:**

- **Country codes** are ISO 3166-1 alpha-2, trimmed and upper-cased, so `" au "` works.
- **Subdivision codes** in lookups and filters are matched exactly, after trimming. Validation is more lenient.
- **Listings omit retired records** unless you pass `includeRetired`. Single-record lookups always return them; check `Retired`.

## Validation

```csharp
ValidationResult r = await directory.ValidateAsync( new AddressCandidate
	{ Country = "AU", Subdivision = "nsw", Locality = "sydney", Postcode = "2000" } );
```

Sal validates **each component on its own, then the combination, and never corrects anything**. Each of `r.Country`, `r.Subdivision`, `r.Locality` and `r.Postcode` is a `ComponentValidation { Outcome, Canonical }`, and `r.Combination` is an outcome too.

| Outcome (`ValidationOutcomes`) | Meaning |
|---|---|
| `VALID` | Matches a current record. `Canonical` is Sal's spelling or code |
| `RETIRED` | Matches only a retired record |
| `UNKNOWN` | Sal holds no such record |
| `MISSING` | The country requires it, and it wasn't given (for example the postcode when `PostcodeRequired`) |
| `MALFORMED` | It doesn't have the country's shape (the postcode fails `PostcodePattern`) |
| `NOT_APPLICABLE` | The country doesn't use it (no subdivisions), or an optional postcode was left blank. A given value is ignored |
| `NOT_CHECKED` | Not evaluated, because something it depends on already failed |
| `MISMATCH` | **Combination only:** each part exists, but they don't belong together |

- **`IsValid`** is true only when every component and the combination are `VALID` or `NOT_APPLICABLE`. **`RETIRED` makes it false.** Decide for yourself whether to accept a retired address, for example on an existing record but not on a new one.
- **The subdivision** may be given as a code (`AU-NSW`), as the code without the country prefix (`NSW`), or as a name (`New South Wales`), case-insensitively.
- **`Match`** is set only when the result is valid. It is the candidate in canonical form, for example `Sydney`, `AU-NSW`, `2000`. **Store `Match`, not what the user typed**, so your data uses Sal's codes and spellings. If the user gave no postcode, `Match.Postcode` stays null, because Sal won't fill one in.
- **`Suggestions`** are up to 10 valid combinations close to the candidate, for example the locality under its real postcodes, or the localities of the given postcode. They are empty when the address is valid, and for a retired country. Present them as choices. Never apply one silently.
- **`DatasetVersion`** is the country version the result was computed against. Record it if you later need to show that an address was valid when you accepted it.

A typical form flow:

1. Validate on submit.
2. If `IsValid`, save `Match`.
3. Otherwise, mark each component whose outcome isn't `VALID` or `NOT_APPLICABLE`, and offer `Suggestions`.
4. On `MISMATCH`, say that the parts don't go together, rather than that one is wrong.

## Autocomplete

Call `SearchLocalitiesAsync( country, typed, subdivision, limit )` after a short debounce, once at least a character or two have been typed. Show `Definition.Name`, `Definition.Subdivision` and `Definition.Postcode`. One locality appears once per postcode, because a locality record is one locality/postcode pair, and a postcode can also span subdivisions (AU 2620 is in both NSW and ACT). After the user picks, validation of the chosen triple is `VALID` by construction. Validate on submit anyway, because the data may have changed in between.

## Caching

Every country-scoped read carries the **dataset version** it was read at. `CurrentVersion` strictly increases with every write to that country, imports included. To cache a country locally, for example for offline or high-volume validation:

1. Call `GetLocalitiesAsync( country )` with no filters, plus `GetSubdivisionsAsync( country )`, and keep them with their `DatasetVersion`.
2. Periodically call `GetCountryVersionsAsync()`, a single small response. Reload a country only when its version differs from the cached one.

Over HTTP, reads also return an `ETag`, and `If-None-Match` gets 304. `LocalityDirectoryClient` doesn't send it, so use the version check above, or a raw `HttpClient` for conditional GETs. Don't cache validation results across a version change.

## Errors

The same exceptions are thrown in-process and over HTTP:

| Exception | When | HTTP |
|---|---|---|
| `Pug.Sal.Abstractions.AccessDeniedException` | The caller lacks `VIEW` (or fails the host's scope or policy). In-process, `Operation` names the refused permission. Over HTTP it is null | 403 |
| `System.Security.Authentication.AuthenticationException` | No or invalid credentials. HTTP only | 401 |
| `UnknownCountryException` | The country doesn't exist (validation included) | 404 |
| `UnknownSubdivisionException`, `UnknownLocalityException` | A single-record lookup found nothing. Listings and filters just return empty | 404 |
| `ArgumentException` (including `ArgumentOutOfRangeException`) | Blank required input, empty search text, or `limit` < 1 | 400 |

In-process, a call made where `ISecurityManager` has no current principal (a background thread outside a request), or only an unauthenticated one, is refused with `AccessDeniedException`. See `sal-host`.
