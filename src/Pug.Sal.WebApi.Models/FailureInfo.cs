using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.WebApi.Models
{
	// Body of a 404, 409 or 422 response: which domain failure occurred and what it concerned.
	// Pug.Sal.Client rebuilds the facade's exception from it (Unknown*Exception, DuplicateEntryException,
	// UnknownDatasetImporterException, DatasetImportException). Only the members its Type uses are set.
	[DataContract]
	public record FailureInfo
	{
		// One of FailureTypes.
		[Required, DataMember( IsRequired = true )]
		public string Type
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// The facade exception's message.
		[DataMember]
		public string Message
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[DataMember]
		public string Country
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[DataMember]
		public string Subdivision
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[DataMember]
		public string Postcode
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Locality name, for UNKNOWN_LOCALITY.
		[DataMember]
		public string Name
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// For DUPLICATE_ENTRY: one of Models.ChangeEntityTypes.
		[DataMember]
		public string EntityType
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// For DUPLICATE_ENTRY: the duplicated key.
		[DataMember]
		public string Key
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// For UNKNOWN_IMPORTER: the importer name.
		[DataMember]
		public string Importer
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// For INVALID_DATASET: the 1-based position in the dataset, when the failure concerns one.
		[DataMember]
		public long? Position
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
