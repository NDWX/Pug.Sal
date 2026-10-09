using System.Collections.Generic;

namespace Pug.Sal.Abstractions
{
	// The importers available in this deployment, keyed by name (case-insensitive). Ships empty:
	// the host registers the importers it uses.
	public interface IDatasetImporterRegistry
	{
		IEnumerable<string> Names { get; }

		// The named importer, or null when none is registered under that name.
		IDatasetImporter GetImporter( string name );
	}
}
