using Pug.Application.Data;
using Pug.Application.Security;
using Pug.Sal.Abstractions;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// The in-process ILocalityDirectory (DECISIONS D16). Reads the store's full records and
	// publishes them without their audit contexts (ToInfo()); validates address candidates.
	public sealed class LocalityDirectory : LocalityServiceBase, ILocalityDirectory
	{
		public LocalityDirectory( IApplicationData<IDataSession> applicationData,
								ISecurityManager securityManager,
								INameNormalizer normalizer )
			: base( applicationData, securityManager, normalizer )
		{
		}

		public async Task<IEnumerable<CountryInfo>> GetCountriesAsync( bool includeRetired = false ) =>
			( await ReadCountriesAsync( includeRetired ).ConfigureAwait( false ) )
			.Select( country => country.ToInfo() )
			.ToList();

		public Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync() => ReadCountryVersionsAsync();

		public async Task<CountryInfo> GetCountryAsync( string country ) =>
			( await ReadCountryAsync( country ).ConfigureAwait( false ) ).ToInfo();

		public async Task<Versioned<IEnumerable<SubdivisionInfo>>> GetSubdivisionsAsync( string country, bool includeRetired = false ) =>
			Project( await ReadSubdivisionsAsync( country, includeRetired ).ConfigureAwait( false ), subdivision => subdivision.ToInfo() );

		public async Task<Versioned<SubdivisionInfo>> GetSubdivisionAsync( string country, string subdivision )
		{
			Versioned<Subdivision> read = await ReadSubdivisionAsync( country, subdivision ).ConfigureAwait( false );

			return new Versioned<SubdivisionInfo> { Value = read.Value.ToInfo(), DatasetVersion = read.DatasetVersion };
		}

		public async Task<Versioned<IEnumerable<LocalityInfo>>> GetLocalitiesAsync( string country, string subdivision = null!,
																					string postcode = null!, string name = null!,
																					bool includeRetired = false ) =>
			Project( await ReadLocalitiesAsync( country, subdivision, postcode, name, includeRetired ).ConfigureAwait( false ),
					locality => locality.ToInfo() );

		public async Task<Versioned<LocalityInfo>> GetLocalityAsync( string country, string subdivision, string postcode, string name )
		{
			Versioned<Locality> read = await ReadLocalityAsync( country, subdivision, postcode, name ).ConfigureAwait( false );

			return new Versioned<LocalityInfo> { Value = read.Value.ToInfo(), DatasetVersion = read.DatasetVersion };
		}

		public async Task<Versioned<IEnumerable<LocalityInfo>>> SearchLocalitiesAsync( string country, string text,
																						string subdivision = null!, int limit = 20 ) =>
			Project( await SearchAsync( country, text, subdivision, limit ).ConfigureAwait( false ), locality => locality.ToInfo() );

		public async Task<ValidationResult> ValidateAsync( AddressCandidate candidate )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			if( candidate is null )
				throw new ArgumentNullException( nameof(candidate) );

			string code = CountryCode( candidate.Country );
			string? nameKey = NameKey( candidate.Locality );
			string? postcodeKey = Normalizer.NormalizePostcode( candidate.Postcode );

			Versioned<AddressEvidence> evidence = await ReadVersionedAsync(
														code,
														async ( session, country ) =>
														{
															IEnumerable<Subdivision> subdivisions = country.Definition.HasSubdivisions
																? await session.GetSubdivisionsAsync( code, true ).ConfigureAwait( false )
																: Enumerable.Empty<Subdivision>();

															IEnumerable<Locality> byName = nameKey is null
																? Enumerable.Empty<Locality>()
																: await session.GetLocalitiesAsync( code, null, null, nameKey, true ).ConfigureAwait( false );

															IEnumerable<Locality> byPostcode = postcodeKey is null
																? Enumerable.Empty<Locality>()
																: await session.GetLocalitiesAsync( code, null, postcodeKey, null, true ).ConfigureAwait( false );

															return new AddressEvidence( country, subdivisions.ToList(), byName.ToList(), byPostcode.ToList() );
														}
													)
													.ConfigureAwait( false );

			return AddressValidation.Validate( candidate, evidence.Value, Normalizer );
		}

		private static Versioned<IEnumerable<TOut>> Project<TIn, TOut>( Versioned<IEnumerable<TIn>> read, Func<TIn, TOut> project ) =>
			new() { Value = read.Value.Select( project ).ToList(), DatasetVersion = read.DatasetVersion };
	}
}
