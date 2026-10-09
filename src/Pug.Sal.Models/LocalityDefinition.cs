using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// Maintainer-authored description of one locality/postcode pair. (Country, Subdivision,
	// normalized Postcode, normalized Name) is the key: a suburb served by several postcodes is one
	// record per postcode, and same-named places within a subdivision are told apart by postcode. A
	// locality is never updated: a correction is a delete and a create.
	[DataContract]
	public record LocalityDefinition
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

		// Code of the owning subdivision; required when the country HasSubdivisions, and must be
		// null otherwise.
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

		// Unique only within the country; matching uses its normalized form. Required when the
		// country's PostcodeRequired; null in countries without postcodes.
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

		// Display name, stored as given; matching uses its normalized form.
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
	}
}
