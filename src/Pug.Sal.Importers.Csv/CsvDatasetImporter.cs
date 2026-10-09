using System.Text;
using Pug.Sal.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal.Importers.Csv
{
	// Reads an RFC 4180 CSV dataset (quoted fields, doubled quotes, line breaks inside quotes; UTF-8
	// unless a byte order mark says otherwise) whose first record is a header. Columns are found by
	// header name, case-insensitively. Blank lines are skipped, values are trimmed, and every record
	// must have as many fields as the header. Positions in errors are 1-based line numbers.
	//
	// Options (ImportRequest.Options, keys case-insensitive):
	// - "locality": header of the locality name column (default "locality"; the column is required).
	// - "postcode": header of the postcode column (default "postcode").
	// - "subdivision": header of the subdivision code column (default "subdivision").
	//   A postcode or subdivision column named by option must exist; when left to its default and
	//   absent, every row's value is null (countries without postcodes or subdivisions).
	// - "subdivisionPrefix": prepended to each subdivision value not already starting with it, e.g.
	//   "AU-" turns a dataset's "NSW" into "AU-NSW".
	// - "delimiter": one character, or "tab" (default ",").
	public sealed class CsvDatasetImporter : IDatasetImporter
	{
		// The name hosts conventionally register this importer under.
		public const string DefaultName = "csv";

		public async IAsyncEnumerable<ImportRow> ReadAsync( string country, Stream source, IDictionary<string, string> options )
		{
			if( source is null )
				throw new ArgumentNullException( nameof(source) );

			CsvOptions settings = CsvOptions.From( options );

			using StreamReader text = new( source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true );
			CsvRecordReader reader = new( text, settings.Delimiter );

			List<string> header = await reader.ReadAsync().ConfigureAwait( false ) ??
								throw new DatasetImportException( "The dataset is empty: a header record is required." );

			long headerLine = reader.RecordStart;
			int locality = Column( header, settings.Locality, true, headerLine );
			int postcode = Column( header, settings.Postcode, settings.PostcodeGiven, headerLine );
			int subdivision = Column( header, settings.Subdivision, settings.SubdivisionGiven, headerLine );

			while( await reader.ReadAsync().ConfigureAwait( false ) is { } fields )
			{
				if( fields.Count != header.Count )
					throw new DatasetImportException( $"The record has {fields.Count} fields; the header has {header.Count}.", reader.RecordStart );

				yield return new ImportRow
				{
					Locality = Value( fields, locality )!,
					Postcode = Value( fields, postcode )!,
					Subdivision = Prefixed( Value( fields, subdivision ), settings.SubdivisionPrefix )!
				};
			}
		}

		// The column's index, or -1 when it is absent and optional.
		private static int Column( List<string> header, string name, bool required, long line )
		{
			int index = header.FindIndex( column => string.Equals( column.Trim(), name, StringComparison.OrdinalIgnoreCase ) );

			if( index < 0 && required )
				throw new DatasetImportException( $"The header has no '{name}' column.", line );

			return index;
		}

		private static string? Value( List<string> fields, int index )
		{
			if( index < 0 )
				return null;

			string value = fields[ index ].Trim();

			return value.Length == 0 ? null : value;
		}

		private static string? Prefixed( string? value, string? prefix ) =>
			value is null || string.IsNullOrEmpty( prefix ) || value.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )
				? value
				: prefix + value;

		private sealed record CsvOptions( char Delimiter, string Locality, string Postcode, bool PostcodeGiven, string Subdivision,
										bool SubdivisionGiven, string? SubdivisionPrefix )
		{
			public static CsvOptions From( IDictionary<string, string>? options )
			{
				Dictionary<string, string> given = new( StringComparer.OrdinalIgnoreCase );

				if( options is not null )
				{
					foreach( KeyValuePair<string, string> option in options )
						given[ option.Key ] = option.Value;
				}

				string? Get( string key ) =>
					given.TryGetValue( key, out string? value ) && !string.IsNullOrWhiteSpace( value ) ? value.Trim() : null;

				string? postcode = Get( "postcode" );
				string? subdivision = Get( "subdivision" );

				return new CsvOptions( ParseDelimiter( given.TryGetValue( "delimiter", out string? delimiter ) ? delimiter : null ),
										Get( "locality" ) ?? "locality",
										postcode ?? "postcode",
										postcode is not null,
										subdivision ?? "subdivision",
										subdivision is not null,
										Get( "subdivisionPrefix" ) );
			}

			private static char ParseDelimiter( string? value )
			{
				if( string.IsNullOrEmpty( value ) )
					return ',';

				if( string.Equals( value, "tab", StringComparison.OrdinalIgnoreCase ) )
					return '\t';

				if( value.Length != 1 || value[ 0 ] == '"' || value[ 0 ] == '\r' || value[ 0 ] == '\n' )
					throw new DatasetImportException( $"The delimiter option must be one character other than a quote or line break, or \"tab\"; got '{value}'." );

				return value[ 0 ];
			}
		}
	}
}
