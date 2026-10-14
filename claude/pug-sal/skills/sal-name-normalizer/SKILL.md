---
name: sal-name-normalizer
description: Use when replacing Pug.Sal's default INameNormalizer, i.e. changing how locality names and postcodes are matched, searched and keyed (abbreviations such as St/Saint, transliteration, punctuation, postcode formats), or when judging whether a custom normalizer is safe for an existing Sal store.
---

# Writing a Pug.Sal name normalizer

`INameNormalizer` (`Pug.Sal.Abstractions`) produces the forms that names and postcodes are matched by:

```csharp
public interface INameNormalizer
{
	string Normalize( string name );                 // null in, null out
	string NormalizePostcode( string postcode );     // null for null or whitespace
}
```

The default, `Pug.Sal.NameNormalizer` (DECISIONS D4), does the following:

- **Names:** trims, collapses internal whitespace to one space, strips diacritics (FormD, then drops non-spacing marks), and upper-cases invariantly. `"  st  kílda "` becomes `"ST KILDA"`.
- **Postcodes:** trims, collapses whitespace and upper-cases. `"sw1a  1aa"` becomes `"SW1A 1AA"`.

## Read this before writing one

**The normalized forms are stored as part of each locality's identity.** The key is `(country, subdivision, NormalizePostcode( postcode ), Normalize( name ))`. Every write, import, lookup, search and validation recomputes the key with the current normalizer and compares it with the stored one. So:

- **Choose the normalizer before the store holds any localities.** A normalizer that produces different keys for existing data makes those rows unreachable by name or postcode. Writes then create duplicates beside them instead of matching them.
- **Sal has no re-normalization tool.** On a populated store, the only way to re-key is to re-import every country with the new normalizer. That works because an import makes the country match its dataset (D13). But it **deletes** localities that aren't in the dataset, manual additions included, and it re-registers the rest. If the store holds manual localities you need to keep, don't swap the normalizer: raise the need for a re-normalize operation in Pug.Sal instead.
- **It has no country parameter.** Rules apply to every country. A per-country rule, such as expanding `St` to `Saint` only in AU, can't be expressed through this interface. D4 lists per-country abbreviation rules as a future extension. Don't fake it by guessing the country from the name.

If what you need is fuzzier *suggestions* rather than different *identity*, a normalizer is the wrong tool. Suggestions come from validation and search, not from the keys.

## Properties a normalizer must have

Each of these is relied on somewhere in Sal:

1. **Deterministic and culture-invariant.** Use the same output on every machine and in every culture: `ToUpperInvariant` / `ToLowerInvariant`, never `CurrentCulture`. Keys written by one host are read by others.
2. **Idempotent.** `Normalize( Normalize( x ) ) == Normalize( x )`, and the same holds for postcodes.
3. **Prefix-preserving for names.** Search normalizes what the user typed and matches it as a **prefix** of stored name keys. If `p` is a prefix of `name`, `Normalize( p )` must be a prefix of `Normalize( name )`, at least at word boundaries. A whole-word rule can break this for a partly typed word. With `ST` rewritten to `SAINT`, typing `St` (the start of `Stanley`) normalizes to `SAINT`, which no longer prefixes `STANLEY`. Test your rules against partial input.
4. **Null and blank in, null or empty out.** `Normalize( null )` returns null. A name that normalizes to `""` is treated as missing, and is refused. `NormalizePostcode` returns null for null or whitespace, meaning "no postcode".
5. **Postcodes still match each country's `PostcodePattern`.** The pattern is matched against the **normalized** postcode. If you change the postcode form, for example by removing the space in `SW1A 1AA`, update the patterns of every country you maintain.
6. **Fast and allocation-light.** It runs once per row on every import, which can be hundreds of thousands of rows, and on every validation.
7. **Thread-safe.** It is a singleton shared by both facades.

## Implementing and registering

Implement it in your own assembly, referencing only `Pug.Sal.Abstractions`. If the change is a small adjustment to the default, delegate to or wrap the default rather than reimplementing it. `Pug.Sal.NameNormalizer` is sealed, so compose it. You may then reference `Pug.Sal`.

```csharp
public sealed class SaintNormalizer : INameNormalizer
{
	private readonly NameNormalizer _default = new();

	public string? Normalize( string? name ) =>
		_default.Normalize( name ) is { } key ? Expand( key ) : null;

	public string? NormalizePostcode( string? postcode ) => _default.NormalizePostcode( postcode );

	private static string Expand( string key ) => /* word-level rules on the normalized form */ key;
}
```

Register it with `AddSingleton`, either before or after `AddSal()`. `AddSal` uses `TryAdd`, and the last registration wins. Don't register it with `TryAddSingleton` after `AddSal`, because it would be ignored.

```csharp
services.AddSingleton<INameNormalizer, SaintNormalizer>();
services.AddSal();
```

## Testing

Write unit tests for properties 1 to 5: idempotence, prefix preservation on real names from your datasets, null and blank handling, and every maintained country's postcode pattern against normalized sample postcodes. `tests/Pug.Sal.Tests/NameNormalizerTests.cs` in the Pug.Sal repository shows the cases the default covers. Then run an import of a real dataset into an empty store with the new normalizer, and check search and validation on names the rules affect.
