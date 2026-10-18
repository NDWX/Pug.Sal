using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// A category assigned to a country, as ILocalityDirectory publishes it: without the audit
	// contexts, which only CountryCategory (from ILocalityAdministration) carries.
	[DataContract]
	public record CountryCategoryInfo
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

		// Code of the global category.
		[Required, DataMember( IsRequired = true )]
		public string Category
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// The global category's name, as it is now.
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

		// Null while enabled. Localities can be created or imported only under an enabled category;
		// a disabled one is still held by the localities that had it, unless they were moved to a
		// replacement.
		[DataMember]
		public DateTime? Disabled
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
