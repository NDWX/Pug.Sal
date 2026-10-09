using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// Outcome of validating one address component on its own.
	[DataContract]
	public record ComponentValidation
	{
		// One of ValidationOutcomes.
		[Required, DataMember( IsRequired = true )]
		public string Outcome
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// The component as Sal holds it (canonical code, spelling and casing) when it was matched;
		// null otherwise.
		[DataMember]
		public string Canonical
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
