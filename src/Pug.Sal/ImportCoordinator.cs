using Pug.Effable;
using Pug.Sal.Abstractions;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// Applies one parsed dataset to one country, inside the caller's transaction, after the caller
	// took the country's lock and version (DECISIONS D13). Every row is checked before anything is
	// written, so an invalid row throws DatasetImportException with nothing applied; the caller's
	// transaction rolls back the version bump with it.
	internal sealed class ImportCoordinator
	{
		private readonly INameNormalizer _normalizer;

		public ImportCoordinator( INameNormalizer normalizer )
		{
			_normalizer = normalizer;
		}

		// action's timestamp is the version the import produces.
		public async Task<ImportRun> ApplyAsync( IDataSession session, string countryCode, string importer, string? source,
												IReadOnlyList<ImportRow> rows, ActionContext<Reference> action )
		{
			Country country = await session.GetCountryAsync( countryCode ).ConfigureAwait( false ) ??
							throw new UnknownCountryException( countryCode );

			HashSet<string> subdivisions = country.Definition.HasSubdivisions
												? ( await session.GetSubdivisionsAsync( countryCode, true ).ConfigureAwait( false ) )
												.Select( subdivision => subdivision.Definition.Code )
												.ToHashSet( StringComparer.Ordinal )
												: new HashSet<string>();

			// Assigned categories, by code: whether each is enabled (D28).
			Dictionary<string, bool> categories = ( await session.GetCountryCategoriesAsync( countryCode, true ).ConfigureAwait( false ) )
												.ToDictionary( category => category.Category, category => category.Disabled is null,
																StringComparer.Ordinal );

			IReadOnlyList<Entry> entries = Check( country.Definition, subdivisions, categories, rows );

			int added = 0, reinstated = 0, recategorized = 0, unchanged = 0;

			foreach( Entry entry in entries )
			{
				Locality locality = new() { Definition = entry.Definition, Registration = action };

				switch( await session.UpsertImportedLocalityAsync( locality, entry.PostcodeKey, entry.NameKey, action )
									.ConfigureAwait( false ) )
				{
					case LocalityUpsertOutcome.Added:
						added++;
						break;

					case LocalityUpsertOutcome.Reinstated:
						reinstated++;
						break;

					case LocalityUpsertOutcome.Recategorized:
						recategorized++;
						break;

					default:
						unchanged++;
						break;
				}
			}

			int deleted = await session.DeleteLocalitiesStampedBeforeAsync( countryCode, action.Timestamp ).ConfigureAwait( false );

			ImportRun run = new()
			{
				Identifier = Guid.NewGuid().ToString( "N" ),
				Country = countryCode,
				Importer = importer,
				Source = source!,
				Added = added,
				Reinstated = reinstated,
				Deleted = deleted,
				Recategorized = recategorized,
				Unchanged = unchanged,
				DatasetVersion = action.Timestamp,
				Performed = action
			};

			await session.InsertImportRunAsync( run ).ConfigureAwait( false );

			return run;
		}

		private sealed record Entry( LocalityDefinition Definition, string? PostcodeKey, string NameKey );

		// The dataset's distinct localities in first-seen order; a row repeating an earlier row's key
		// is the same locality, and the first row's display values are kept, but it must give the
		// same category. Positions are 1-based row numbers.
		private IReadOnlyList<Entry> Check( CountryDefinition country, HashSet<string> subdivisions, Dictionary<string, bool> categories,
											IReadOnlyList<ImportRow> rows )
		{
			if( rows.Count == 0 )
				throw new DatasetImportException(
					$"The dataset holds no rows; importing it would delete every locality of country '{country.Code}'." );

			List<Entry> entries = new( rows.Count );
			Dictionary<LocalityKey, string> seen = new();

			for( int index = 0; index < rows.Count; index++ )
			{
				ImportRow row = rows[ index ];
				long position = index + 1;

				string? name = string.IsNullOrWhiteSpace( row.Locality ) ? null : row.Locality.Trim();
				string? nameKey = name is null ? null : _normalizer.Normalize( name );

				if( string.IsNullOrEmpty( nameKey ) )
					throw new DatasetImportException( "The locality name is missing.", position );

				string? subdivision = string.IsNullOrWhiteSpace( row.Subdivision ) ? null : row.Subdivision.Trim();

				if( country.HasSubdivisions )
				{
					if( subdivision is null )
						throw new DatasetImportException( $"The subdivision of '{name}' is missing.", position );

					if( !subdivisions.Contains( subdivision ) )
						throw new DatasetImportException(
							$"Unknown subdivision '{subdivision}' of country '{country.Code}'; imports never create subdivisions.", position );
				}
				else if( subdivision is not null )
				{
					throw new DatasetImportException(
						$"Country '{country.Code}' has no subdivisions, but '{name}' names subdivision '{subdivision}'.", position );
				}

				string? postcode = string.IsNullOrWhiteSpace( row.Postcode ) ? null : row.Postcode.Trim();
				string? postcodeKey = _normalizer.NormalizePostcode( postcode! );
				string? problem = PostcodeRules.Problem( country, postcodeKey );

				if( problem is not null )
					throw new DatasetImportException( $"{problem} ('{name}')", position );

				string? category = string.IsNullOrWhiteSpace( row.Category ) ? null : row.Category.Trim();

				if( category is null )
					throw new DatasetImportException( $"The category of '{name}' is missing.", position );

				if( !categories.TryGetValue( category, out bool enabled ) )
					throw new DatasetImportException( $"Category '{category}' is not assigned to country '{country.Code}'.", position );

				if( !enabled )
					throw new DatasetImportException( $"Category '{category}' is disabled in country '{country.Code}'.", position );

				LocalityKey key = new( country.Code, subdivision, postcodeKey, nameKey );

				if( seen.TryGetValue( key, out string? earlier ) )
				{
					if( earlier != category )
						throw new DatasetImportException(
							$"'{name}' repeats an earlier row's locality with category '{category}' instead of '{earlier}'.", position );

					continue;
				}

				seen.Add( key, category );
				entries.Add( new Entry(
					new LocalityDefinition
					{
						Country = country.Code, Subdivision = subdivision!, Postcode = postcode!, Name = name!, Category = category
					},
					postcodeKey,
					nameKey ) );
			}

			return entries;
		}
	}
}
