using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Pug.Effable;

namespace Pug.Sal.Models
{
	// A global category with everything Sal maintains for it, audit contexts included, as
	// ILocalityAdministration returns it. Project it to CategoryInfo with ToInfo().
	[DataContract]
	public record Category
	{
		[Required, DataMember( IsRequired = true )]
		public CategoryDefinition Definition
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Null while current. A retired category can no longer be assigned to a country, or enabled
		// in one; countries that have it keep it.
		[DataMember]
		public DateTime? Retired
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
