using System;

namespace Pug.Sal.Abstractions
{
	// Thrown when a dataset cannot be read or is inconsistent; the import applied nothing.
	public class DatasetImportException : Exception
	{
		// 1-based position in the source (e.g. line) the problem was found at; null when it
		// concerns the dataset as a whole.
		public long? Position { get; }

		public DatasetImportException( string message, long? position = null, Exception innerException = null )
			: base( position == null ? message : $"{message} (at {position})", innerException )
		{
			Position = position;
		}
	}
}
