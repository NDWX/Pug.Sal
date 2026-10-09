using System.Text;
using Pug.Sal.Abstractions;

namespace Pug.Sal.Importers.Csv
{
	// Reads RFC 4180 records one at a time. A quote opens a quoted field only at the start of a
	// field; inside one, a doubled quote is a literal quote and line breaks are part of the value
	// (as "\n"). Blank lines between records are skipped.
	internal sealed class CsvRecordReader
	{
		private readonly TextReader _reader;

		private readonly char _delimiter;

		private long _line;

		public CsvRecordReader( TextReader reader, char delimiter )
		{
			_reader = reader;
			_delimiter = delimiter;
		}

		// 1-based line number the last record read started on.
		public long RecordStart { get; private set; }

		// The next record's fields, or null at the end of the dataset.
		public async Task<List<string>?> ReadAsync()
		{
			string? line;

			do
			{
				line = await _reader.ReadLineAsync().ConfigureAwait( false );

				if( line is null )
					return null;

				_line++;
			}
			while( string.IsNullOrWhiteSpace( line ) );

			RecordStart = _line;

			List<string> fields = new();
			StringBuilder field = new();
			bool inQuotes = false;
			bool fieldQuoted = false;
			int index = 0;

			while( true )
			{
				if( index >= line.Length )
				{
					if( !inQuotes )
					{
						fields.Add( field.ToString() );

						return fields;
					}

					line = await _reader.ReadLineAsync().ConfigureAwait( false ) ??
							throw new DatasetImportException( "A quoted field is not closed before the end of the dataset.", RecordStart );

					_line++;
					field.Append( '\n' );
					index = 0;

					continue;
				}

				char c = line[ index ];

				if( inQuotes )
				{
					if( c == '"' )
					{
						if( index + 1 < line.Length && line[ index + 1 ] == '"' )
						{
							field.Append( '"' );
							index += 2;

							continue;
						}

						inQuotes = false;
					}
					else
					{
						field.Append( c );
					}
				}
				else if( c == '"' && field.Length == 0 && !fieldQuoted )
				{
					inQuotes = true;
					fieldQuoted = true;
				}
				else if( c == _delimiter )
				{
					fields.Add( field.ToString() );
					field.Clear();
					fieldQuoted = false;
				}
				else
				{
					field.Append( c );
				}

				index++;
			}
		}
	}
}
