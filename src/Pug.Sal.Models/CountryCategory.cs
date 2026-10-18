using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Pug.Effable;

namespace Pug.Sal.Models
{
	// A category assigned to a country, audit contexts included, as ILocalityAdministration returns
	// it. Project it to CountryCategoryInfo with ToInfo().
	[DataContract]
	public record CountryCategory
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

		[Required, DataMember( IsRequired = true )]
		public ActionContext<Reference> Registration
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Null until the record is first modified, retired or reinstated.
		[DataMember]
		public ActionContext<Reference> LastUpdate
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
