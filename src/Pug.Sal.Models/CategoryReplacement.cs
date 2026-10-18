using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Pug.Effable;

namespace Pug.Sal.Models
{
	// The localities of one country moved from a category to its replacement by
	// ILocalityAdministration.DisableCategoryAsync. It is the replacement's event: after commit it
	// is published through ILocalityChangeEventPublisher.PublishCategoryReplacementAsync, only when
	// at least one locality moved; the moved localities publish no LocalityChange (DECISIONS D28).
	[DataContract]
	public record CategoryReplacement
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

		// The disabled category the localities held.
		[Required, DataMember( IsRequired = true )]
		public string Category
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// The category they hold now.
		[Required, DataMember( IsRequired = true )]
		public string Replacement
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// How many localities moved, retired ones included; at least one.
		[Required, DataMember( IsRequired = true )]
		public int Localities
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// UTC country dataset version the replacement produced, and the timestamp of Performed and
		// of each moved locality's LastUpdate.
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
	}
}
