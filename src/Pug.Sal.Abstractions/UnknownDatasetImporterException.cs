using System;

namespace Pug.Sal.Abstractions
{
	// Thrown when an ImportRequest names an importer not registered in this deployment.
	public class UnknownDatasetImporterException : Exception
	{
		public string Importer { get; }

		public UnknownDatasetImporterException( string importer )
			: base( $"Unknown dataset importer '{importer}'" )
		{
			Importer = importer;
		}
	}
}
