using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.Models
{
	// Result of validating an AddressCandidate: each component on its own, then the combination.
	// Sal never auto-corrects; Match and Suggestions are for the caller to act on.
	[DataContract]
	public record ValidationResult
	{
		// True when every component and the combination are VALID or NOT_APPLICABLE.
		[Required, DataMember( IsRequired = true )]
		public bool IsValid
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[Required, DataMember( IsRequired = true )]
		public ComponentValidation Country
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[Required, DataMember( IsRequired = true )]
		public ComponentValidation Subdivision
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[Required, DataMember( IsRequired = true )]
		public ComponentValidation Locality
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[Required, DataMember( IsRequired = true )]
		public ComponentValidation Postcode
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// VALID, MISMATCH when the components exist but do not belong together, or NOT_CHECKED when
		// a component already failed.
		[Required, DataMember( IsRequired = true )]
		public string Combination
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// The matched combination, in canonical form, when IsValid; null otherwise.
		[DataMember]
		public AddressCandidate Match
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// Valid combinations close to the candidate (e.g. the locality under its actual postcodes);
		// empty when IsValid or when Sal has none.
		[Required, DataMember( IsRequired = true )]
		public IEnumerable<AddressCandidate> Suggestions
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// UTC country dataset version the validation was made against.
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
