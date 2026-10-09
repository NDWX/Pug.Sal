using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// One locality/postcode pair read from a dataset by an IDatasetImporter: exactly one
	// LocalityDefinition. A row repeating an earlier row's key is the same locality.
	[DataContract]
	public record ImportRow
	{
		// Code of an existing subdivision, current or retired; required when the country
		// HasSubdivisions and must be null otherwise. An unknown subdivision fails the whole
		// import: imports never create subdivisions. A retired one is accepted and stays retired.
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

		[Required, DataMember( IsRequired = true )]
		public string Locality
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
	}
}
