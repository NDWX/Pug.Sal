using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// The address components an application asks Sal to validate, or a canonical/suggested
	// combination Sal answers with.
	[DataContract]
	public record AddressCandidate
	{
		// ISO 3166-1 alpha-2.
		[Required, DataMember( IsRequired = true )]
		public string Country
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Subdivision code or name.
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
