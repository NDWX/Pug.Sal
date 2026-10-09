using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// Maintainer-authored description of a state, territory, province or similar. (Country, Code)
	// is the key and is immutable once created.
	[DataContract]
	public record SubdivisionDefinition
	{
		// ISO 3166-1 alpha-2 of the owning country.
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

		// Maintainer-defined, unique within the country. ISO 3166-2 (e.g. AU-NSW) is the
		// recommended convention but is not enforced, so codes such as AU-JBT for territories
		// without one are accepted.
		[Required, DataMember( IsRequired = true )]
		public string Code
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[Required, DataMember( IsRequired = true )]
		public string Name
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Free-form kind of subdivision; see SubdivisionTypes for the well-known values.
		[DataMember]
		public string Type
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
