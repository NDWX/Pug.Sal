using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// A read result together with the country dataset version it was read at. The version is read
	// in the same session as the value, so it never runs ahead of the data it describes.
	[DataContract]
	public record Versioned<T>
	{
		[Required, DataMember( IsRequired = true )]
		public T Value
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// UTC; see CountryInfo.CurrentVersion.
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
