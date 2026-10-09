using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// A country as ILocalityDirectory publishes it: without the audit contexts, which only Country
	// (from ILocalityAdministration) carries.
	[DataContract]
	public record CountryInfo
	{
		[Required, DataMember( IsRequired = true )]
		public CountryDefinition Definition
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// UTC dataset version of everything under this country. Bumped in the same transaction as
		// every write touching the country (once per import) and strictly increasing, so a client
		// cache keyed by it can revalidate cheaply.
		[Required, DataMember( IsRequired = true )]
		public DateTime CurrentVersion
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
