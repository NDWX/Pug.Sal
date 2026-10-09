using System;

namespace Pug.Sal.Abstractions
{
	// Thrown when a create would duplicate an existing record, retired ones included: reinstate
	// that record instead.
	public class DuplicateEntryException : Exception
	{
		// One of Models.ChangeEntityTypes.
		public string EntityType { get; }

		// Country code, subdivision code, or locality subdivision/postcode/name, per EntityType.
		public string Key { get; }

		public DuplicateEntryException( string entityType, string key )
			: base( $"{entityType} '{key}' already exists" )
		{
			EntityType = entityType;
			Key = key;
		}
	}
}
