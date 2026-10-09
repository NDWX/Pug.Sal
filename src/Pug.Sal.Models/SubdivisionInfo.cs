using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// A subdivision as ILocalityDirectory publishes it: without the audit contexts, which only
	// Subdivision (from ILocalityAdministration) carries.
	[DataContract]
	public record SubdivisionInfo
	{
		[Required, DataMember( IsRequired = true )]
		public SubdivisionDefinition Definition
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
