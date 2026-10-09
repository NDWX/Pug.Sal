using Pug.Sal.Abstractions;

namespace Pug.Sal
{
	// The default IDatasetImporterRegistry: importers registered by name, matched
	// case-insensitively. Starts empty (DECISIONS D9).
	public sealed class DatasetImporterRegistry : IDatasetImporterRegistry
	{
		private readonly Dictionary<string, IDatasetImporter> _importers = new( StringComparer.OrdinalIgnoreCase );

		public DatasetImporterRegistry()
		{
		}

		public DatasetImporterRegistry( IEnumerable<KeyValuePair<string, IDatasetImporter>> importers )
		{
			foreach( KeyValuePair<string, IDatasetImporter> importer in importers )
				Register( importer.Key, importer.Value );
		}

		public IEnumerable<string> Names => _importers.Keys.ToList();

		// Throws ArgumentException when the name is blank or already registered.
		public void Register( string name, IDatasetImporter importer )
		{
			if( string.IsNullOrWhiteSpace( name ) )
				throw new ArgumentException( "An importer name is required.", nameof(name) );

			if( !_importers.TryAdd( name.Trim(), importer ?? throw new ArgumentNullException( nameof(importer) ) ) )
				throw new ArgumentException( $"An importer named '{name}' is already registered.", nameof(name) );
		}

		public IDatasetImporter? GetImporter( string? name ) =>
			name is not null && _importers.TryGetValue( name.Trim(), out IDatasetImporter? importer ) ? importer : null;
	}
}
