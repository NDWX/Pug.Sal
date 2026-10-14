using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Pug.Effable;

namespace Pug.Sal.Models
{
	// A committed register, update, retire, reinstate or delete made through the admin API,
	// published to the host's ILocalityChangeEventPublisher for audit. An import is a different
	// event: it publishes its ImportRun, and none of its rows publishes a LocalityChange.
	[DataContract]
	public record LocalityChange
	{
		[Required, DataMember( IsRequired = true )]
		public string Country
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// One of ChangeEntityTypes.
		[Required, DataMember( IsRequired = true )]
		public string EntityType
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// For a locality change, the locality's subdivision code (null in countries without
		// subdivisions); null otherwise.
		[DataMember]
		public string Subdivision
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// For a locality change, the locality's postcode (null when it has none); null otherwise.
		[DataMember]
		public string Postcode
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Per EntityType: the country code, the subdivision code, or the locality name.
		[Required, DataMember( IsRequired = true )]
		public string Key
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// One of ChangeKinds.
		[Required, DataMember( IsRequired = true )]
		public string Kind
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[Required, DataMember( IsRequired = true )]
		public ActionContext<Reference> Performed
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// UTC country dataset version the change produced.
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
