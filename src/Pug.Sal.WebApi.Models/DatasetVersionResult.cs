using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.WebApi.Models
{
	// Body of a write that returns only the country dataset version it produced (a locality deletion).
	[DataContract]
	public record DatasetVersionResult
	{
		// UTC.
		[Required, DataMember( IsRequired = true )]
		public DateTime DatasetVersion
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
