using System.Collections.Generic;
using System.IO;
using Pug.Sal.Models;

namespace Pug.Sal.Abstractions
{
	// Reads one dataset format into ImportRow values for one country. Importers parse only:
	// matching, creation and retirement are the ImportCoordinator's. Whether a dataset may be
	// loaded at all (licensing) is the host's concern, not the importer's.
	public interface IDatasetImporter
	{
		// Rows of the dataset in source order. Throws DatasetImportException, naming the position
		// in the source, when the dataset cannot be read; options are the ImportRequest's.
		IAsyncEnumerable<ImportRow> ReadAsync( string country, Stream source, IDictionary<string, string> options );
	}
}
