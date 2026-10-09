using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// A country's current dataset version, as returned by the cheap revalidation read.
	[DataContract]
	public record CountryVersion
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

		// UTC.
		[Required, DataMember( IsRequired = true )]
		public DateTime Version
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
