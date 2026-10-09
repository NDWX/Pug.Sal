using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Pug.Effable;

namespace Pug.Sal.Models
{
	// A locality with the state Sal maintains for it.
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

		// Null until the locality is first retired, reinstated or confirmed by an import. Every
		// import re-stamps each locality its dataset holds with the import's context, whose
		// timestamp is the import's dataset version.
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
