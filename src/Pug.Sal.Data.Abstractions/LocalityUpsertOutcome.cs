namespace Pug.Sal.Data.Abstractions
{
	// What IDataSession.UpsertImportedLocalityAsync did to the dataset row's locality.
	public enum LocalityUpsertOutcome
	{
		// The locality did not exist and was inserted.
		Added,

		// The locality existed retired; it was made current again and re-stamped.
		Reinstated,

		// The locality existed and was current; only its LastUpdate was re-stamped.
		Unchanged
	}
}
