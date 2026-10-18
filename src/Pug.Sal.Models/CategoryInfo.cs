using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// A global category as ILocalityDirectory publishes it: without the audit contexts, which only
	// Category (from ILocalityAdministration) carries.
	[DataContract]
	public record CategoryInfo
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
	}
}
