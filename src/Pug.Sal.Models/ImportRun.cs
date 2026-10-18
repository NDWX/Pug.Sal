using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Pug.Effable;

namespace Pug.Sal.Models
{
	// Record of one completed bulk import. It is also the import event: after commit it is
	// published, as is, through ILocalityChangeEventPublisher.PublishImportAsync. Imports publish
	// no LocalityChange.
	[DataContract]
	public record ImportRun
	{
		[Required, DataMember( IsRequired = true )]
		public string Identifier
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

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

		[Required, DataMember( IsRequired = true )]
		public string Importer
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[DataMember]
		public string Source
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Localities created.
		[Required, DataMember( IsRequired = true )]
		public int Added
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Retired localities the dataset holds, made current again.
		[Required, DataMember( IsRequired = true )]
		public int Reinstated
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Localities deleted because the dataset no longer contains them.
		[Required, DataMember( IsRequired = true )]
		public int Deleted
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Current localities the dataset holds under a different category, which it changed to the
		// dataset's.
		[Required, DataMember( IsRequired = true )]
		public int Recategorized
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Current localities the dataset holds under the same category; only their LastUpdate is
		// re-stamped.
		[Required, DataMember( IsRequired = true )]
		public int Unchanged
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// UTC country dataset version the import produced, and the timestamp of Performed,
		// Registration of every locality it created and LastUpdate of every locality it holds.
		// Every import bumps it.
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
