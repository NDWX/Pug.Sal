using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// Parameters of a bulk import into one country; the dataset itself travels separately as a
	// stream. A dataset always covers the whole country: whatever it does not contain is deleted.
	[DataContract]
	public record ImportRequest
	{
		// Name of a registered IDatasetImporter.
		[Required, DataMember( IsRequired = true )]
		public string Importer
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Free-form label of the dataset (e.g. file name and edition), recorded on the ImportRun.
		[DataMember]
		public string Source
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Importer-specific options, e.g. column mappings.
		[DataMember]
		public IDictionary<string, string> Options
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}
	}
}
