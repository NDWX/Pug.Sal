namespace Pug.Sal.Data.Abstractions
{
	// What IDataSession.UpsertImportedLocalityAsync did to the dataset row's locality.
	public enum LocalityUpsertOutcome
	{
		// The locality did not exist and was inserted.
		Added,

		// The locality existed retired; it was made current again, given the row's category and
		// re-stamped.
		Reinstated,

		// The locality existed, current, under another category; it was given the row's category
		// and re-stamped.
		Recategorized,

		// The locality existed, current, under the row's category; only its LastUpdate was
		// re-stamped.
		Unchanged
	}
}
