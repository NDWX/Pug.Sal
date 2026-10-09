using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// A locality as ILocalityDirectory publishes it: without the audit contexts, which only
	// Locality (from ILocalityAdministration) carries.
	[DataContract]
	public record LocalityInfo
	{
		[Required, DataMember( IsRequired = true )]
		public LocalityDefinition Definition
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Null while current. Validation reports a match on a retired record as RETIRED.
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
