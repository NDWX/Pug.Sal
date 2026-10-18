using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// Maintainer-authored description of a country. Code is the key and is immutable once created.
	[DataContract]
	public record CountryDefinition
	{
		// ISO 3166-1 alpha-2, upper case.
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

		// ISO 3166-1 alpha-3, upper case.
		[DataMember]
		public string Alpha3
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

		// Regular expression a postcode of this country must fully match; null accepts any non-
		// empty postcode.
		[DataMember]
		public string PostcodePattern
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Whether an address in this country must carry a postcode.
		[Required, DataMember( IsRequired = true )]
		public bool PostcodeRequired
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Whether localities of this country sit under a subdivision. When false, localities carry
		// no subdivision and validation treats the subdivision component as NOT_APPLICABLE.
		[Required, DataMember( IsRequired = true )]
		public bool HasSubdivisions
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Codes of the current global categories assigned to the country when it is created; at
		// least one. Read only by CreateCountryAsync, and null in what reads return: afterwards a
		// country's categories are managed with the country category operations (DECISIONS D28).
		[DataMember]
		public IEnumerable<string> Categories
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
