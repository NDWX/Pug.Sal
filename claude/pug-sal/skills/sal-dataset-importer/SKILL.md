---
name: sal-dataset-importer
description: Use when writing a Pug.Sal IDatasetImporter, a reader for a locality/postcode dataset format Sal doesn't read out of the box (a vendor's fixed-width file, JSON, XML, a zip of CSVs, an API export), or when deciding whether the built-in CSV importer's column mapping already covers a dataset.
---

# Writing a Pug.Sal dataset importer

An importer turns one dataset into `ImportRow` values for one country:

```csharp
public interface IDatasetImporter
{
	IAsyncEnumerable<ImportRow> ReadAsync( string country, Stream source, IDictionary<string, string> options );
}

public record ImportRow { string Subdivision; string Locality /* required */; string Postcode; }
```

## First check whether CSV is enough

`Pug.Sal.Importers.Csv`'s `CsvDatasetImporter` (registered as `"csv"`) reads RFC 4180 CSV with a header. It finds columns by header name, which you set in options: `locality`, `postcode`, `subdivision` and `delimiter` (one character, or `tab`). Subdivision values are taken as they are, so they must be the registered codes, which exclude the country (`NSW`, not `AU-NSW`). Converting a vendor file to CSV upstream is often simpler than a new importer. Write one when the format isn't tabular text, when rows need joining or decoding, or when the dataset is published in a format you'd otherwise convert by hand on every edition.

## An importer only parses

The importer's job is narrow (DECISIONS D9, D13). `LocalityAdministration.ImportAsync` and its `ImportCoordinator` do everything else:

| The importer does | Sal does (don't duplicate it) |
|---|---|
| Reads the format and yields one `ImportRow` per locality/postcode pair, in source order | Normalizes names and postcodes, and de-duplicates rows by key (the first row's display values win) |
| Maps the source's subdivision values to the **exact codes** Sal stores (ordinal match, so case matters) | Checks that every subdivision exists. An unknown one fails the whole import, because imports never create subdivisions |
| Throws `DatasetImportException( message, position )` when the source can't be read | Checks each postcode against the country's `PostcodeRequired` / `PostcodePattern`, and refuses an empty dataset |
| Leaves values as given (trim if your format pads them) | Applies the import in one transaction: adds, reinstates, deletes whatever is absent, and bumps the country version once |

So an importer never filters, corrects or skips rows to make an import pass. A dataset that contains an unknown subdivision or a malformed postcode **should** fail, with nothing applied. The data maintainer fixes the dataset or the country definition. It also never touches the store, and never decides licensing, which is the host's business.

**A dataset always covers a whole country.** Every locality of the country that the dataset doesn't hold is deleted, manual ones included. An importer that reads part of a file (one state, a delta, "changes since") is wrong by design. A format that ships deltas must be merged into a full edition before it is imported.

## Contract details

- **`country`** is the target country's code, already upper-cased and known to exist. Use it only when the format needs it, for example to pick one country out of a multi-country file.
- **`ImportRow.Subdivision`** is a code, required when the country `HasSubdivisions` and null otherwise. **`Postcode`** is null when the locality has none. **`Locality`** is the display name.
- **`options`** are `ImportRequest.Options`, never null. Over HTTP, they come from the query string as `option.<name>=<value>`. Match keys case-insensitively, as CSV does. Validate them up front, and throw `DatasetImportException` (no position) for a missing or bad one.
- **Positions** are 1-based and mean something to whoever fixes the file: a line number, a record number, or an entry index. Use `null` when the problem concerns the dataset as a whole.
- **The stream** belongs to the caller. Don't dispose it: open readers with `leaveOpen: true`. It may not be seekable (HTTP bodies aren't), so read it forward once. If the format needs random access (zip), copy it to a temporary file or a `MemoryStream` first.
- **Over HTTP**, the body arrives as `application/octet-stream`, `text/csv` or `text/plain`. Any binary format goes as `application/octet-stream`.
- **Sal buffers every row** before it opens the write transaction, so a dataset must fit in memory as `ImportRow`s. Stream your parsing anyway with `yield return`. Don't build a second copy.
- **Encoding:** default to UTF-8, honouring a byte order mark, unless the format defines otherwise.
- **`async`:** use `ConfigureAwait( false )`. The importer is a singleton, so keep no state between calls.

## Implementing and registering

Put it in its own package referencing only `Pug.Sal.Abstractions` (with `Pug.Sal.Models`). Name it `Pug.Sal.Importers.<Format>`, following `Pug.Sal.Importers.Csv`, which is the reference implementation to read first. Expose a `DefaultName` constant:

```csharp
public sealed class GnafImporter : IDatasetImporter
{
	public const string DefaultName = "gnaf";

	public async IAsyncEnumerable<ImportRow> ReadAsync( string country, Stream source, IDictionary<string, string> options )
	{
		// parse; for each record:
		yield return new ImportRow { Subdivision = code, Locality = name, Postcode = postcode };
	}
}

services.AddSal();
services.AddSalDatasetImporter<GnafImporter>( GnafImporter.DefaultName );   // name matched case-insensitively
```

Registering two importers under the same name throws. Callers pick an importer by name: `ImportRequest.Importer`, or `?importer=` over HTTP.

## Testing

- **Unit-test `ReadAsync` directly on small in-memory sources:** every option, an empty source, a malformed record (assert the `Position`), a missing required option, and encoding. `tests/Pug.Sal.Tests/CsvDatasetImporterTests.cs` in the Pug.Sal repository is the model.
- **Run one end-to-end import into a SQLite store** through `LocalityAdministration.ImportAsync`. Check the `ImportRun` counts (`Added`, `Reinstated`, `Deleted`, `Unchanged`), and then that a second import of the same dataset reports everything `Unchanged` with `Deleted` 0.
