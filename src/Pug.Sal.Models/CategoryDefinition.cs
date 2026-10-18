using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// Maintainer-authored description of a global locality category. Code is the key and is
	// immutable once created; a category is assigned to a country before its localities can use it
	// (DECISIONS D28).
	[DataContract]
	public record CategoryDefinition
	{
		// Maintainer-defined and matched exactly.
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
	}
}
