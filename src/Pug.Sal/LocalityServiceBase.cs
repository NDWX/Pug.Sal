using Pug.Application.Data;
using Pug.Application.Data.Extensions;
using Pug.Application.Security;
using Pug.Sal.Abstractions;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// What LocalityDirectory and LocalityAdministration share (DECISIONS D16): authorization,
	// argument normalization, and the reads both offer, over the store's full records. The
	// directory projects those with ToInfo(); administration returns them as they are.
	//
	// Every read checks SecurityOperations.View first. A call without a principal is refused (D8).
	public abstract class LocalityServiceBase
	{
		// The most results SearchLocalitiesAsync returns, whatever limit the caller asks for.
		public const int MaximumSearchLimit = 100;

		protected IApplicationData<IDataSession> ApplicationDataProvider { get; }

		protected ISecurityManager SecurityManager { get; }

		protected INameNormalizer Normalizer { get; }

		protected LocalityServiceBase( IApplicationData<IDataSession> applicationData,
										ISecurityManager securityManager,
										INameNormalizer normalizer )
		{
			ApplicationDataProvider = applicationData ?? throw new ArgumentNullException( nameof(applicationData) );
			SecurityManager = securityManager ?? throw new ArgumentNullException( nameof(securityManager) );
			Normalizer = normalizer ?? throw new ArgumentNullException( nameof(normalizer) );
		}

		// Every check is against ResourceQualifiers.AllCountries (D8). Unlike Pug.Scheduling's
		// coordinator, a missing principal is refused, never trusted.
		protected async Task CheckAuthorizationAsync( string operation )
		{
			IPrincipal? principal = SecurityManager.CurrentPrincipal;

			if( principal is null )
				throw new NotAuthorized();

			bool isAuthorized = await principal.IsAuthorizedAsync(
												new Dictionary<string, string>( 0 ),
												operation,
												ResourceQualifiers.AllCountries,
												string.Empty
											)
											.ConfigureAwait( false );

			if( !isAuthorized )
				throw new NotAuthorized();
		}

		// Country codes are ISO 3166-1 alpha-2, held upper case: input is trimmed and upper-cased.
		protected static string CountryCode( string? country )
		{
			if( string.IsNullOrWhiteSpace( country ) )
				throw new ArgumentException( "A country code is required.", nameof(country) );

			return country.Trim().ToUpperInvariant();
		}

		// An optional subdivision code: null when blank, otherwise trimmed and matched exactly.
		protected static string? OptionalCode( string? code ) =>
			string.IsNullOrWhiteSpace( code ) ? null : code.Trim();

		// The normalized name key, or null when the name is null or blank.
		protected string? NameKey( string? name )
		{
			string? key = Normalizer.Normalize( name! );

			return string.IsNullOrEmpty( key ) ? null : key;
		}

		protected LocalityKey KeyOf( string countryCode, string? subdivision, string? postcode, string? name )
		{
			string nameKey = NameKey( name ) ?? throw new ArgumentException( "A locality name is required.", nameof(name) );

			return new LocalityKey( countryCode, OptionalCode( subdivision ), Normalizer.NormalizePostcode( postcode! ), nameKey );
		}

		// Reads the country and then, in the same session, the value, so the version returned is
		// never ahead of the value it describes (D6). Throws UnknownCountryException when the
		// country does not exist. Checks nothing: callers authorize first.
		protected async Task<Versioned<T>> ReadVersionedAsync<T>( string countryCode, Func<IDataSession, Country, Task<T>> read )
		{
			Versioned<T>? result = await ApplicationDataProvider.ExecuteAsync(
											async ( session, context ) =>
											{
												Country? country = await session.GetCountryAsync( context.countryCode ).ConfigureAwait( false );

												if( country is null )
													return (Versioned<T>?)null;

												return new Versioned<T>
												{
													Value = await context.read( session, country ).ConfigureAwait( false ),
													DatasetVersion = country.CurrentVersion
												};
											},
											context: new { countryCode, read }
										)
										.ConfigureAwait( false );

			return result ?? throw new UnknownCountryException( countryCode );
		}

		// == Reads shared by both facades (VIEW) ==

		protected async Task<IEnumerable<Country>> ReadCountriesAsync( bool includeRetired )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			return await ApplicationDataProvider.ExecuteAsync(
							async ( session, context ) =>
								await session.GetCountriesAsync( context.includeRetired ).ConfigureAwait( false ),
							context: new { includeRetired }
						)
						.ConfigureAwait( false );
		}

		protected async Task<IEnumerable<CountryVersion>> ReadCountryVersionsAsync()
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			return await ApplicationDataProvider.ExecuteAsync(
							async ( session, context ) =>
								await session.GetCountryVersionsAsync().ConfigureAwait( false ),
							context: new { }
						)
						.ConfigureAwait( false );
		}

		protected async Task<Country> ReadCountryAsync( string country )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			string code = CountryCode( country );

			Country? found = await ApplicationDataProvider.ExecuteAsync(
									async ( session, context ) =>
										await session.GetCountryAsync( context.code ).ConfigureAwait( false ),
									context: new { code }
								)
								.ConfigureAwait( false );

			return found ?? throw new UnknownCountryException( code );
		}

		protected async Task<Versioned<IEnumerable<Subdivision>>> ReadSubdivisionsAsync( string country, bool includeRetired )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			return await ReadVersionedAsync(
							CountryCode( country ),
							( session, found ) => session.GetSubdivisionsAsync( found.Definition.Code, includeRetired )
						)
						.ConfigureAwait( false );
		}

		protected async Task<Versioned<Subdivision>> ReadSubdivisionAsync( string country, string subdivision )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			string code = CountryCode( country );
			string subdivisionCode = OptionalCode( subdivision ) ??
									throw new ArgumentException( "A subdivision code is required.", nameof(subdivision) );

			Versioned<Subdivision?> read = await ReadVersionedAsync(
												code,
												( session, _ ) => session.GetSubdivisionAsync( code, subdivisionCode )
											)
											.ConfigureAwait( false );

			if( read.Value is null )
				throw new UnknownSubdivisionException( code, subdivisionCode );

			return new Versioned<Subdivision> { Value = read.Value, DatasetVersion = read.DatasetVersion };
		}

		// Filters are applied only when given: subdivision by code, postcode and name by their
		// normalized forms.
		protected async Task<Versioned<IEnumerable<Locality>>> ReadLocalitiesAsync( string country, string? subdivision,
																					string? postcode, string? name,
																					bool includeRetired )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			string code = CountryCode( country );
			string? subdivisionCode = OptionalCode( subdivision );
			string? postcodeKey = Normalizer.NormalizePostcode( postcode! );
			string? nameKey = NameKey( name );

			return await ReadVersionedAsync(
							code,
							( session, _ ) => session.GetLocalitiesAsync( code, subdivisionCode, postcodeKey, nameKey, includeRetired )
						)
						.ConfigureAwait( false );
		}

		protected async Task<Versioned<Locality>> ReadLocalityAsync( string country, string? subdivision, string? postcode, string? name )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			LocalityKey key = KeyOf( CountryCode( country ), subdivision, postcode, name );

			Versioned<Locality?> read = await ReadVersionedAsync(
											key.Country,
											( session, _ ) => session.GetLocalityAsync( key )
										)
										.ConfigureAwait( false );

			if( read.Value is null )
				throw new UnknownLocalityException( key.Country, key.Subdivision!, postcode?.Trim()!, name!.Trim() );

			return new Versioned<Locality> { Value = read.Value, DatasetVersion = read.DatasetVersion };
		}

		// Prefix search on the normalized name over current localities; limit is capped at
		// MaximumSearchLimit.
		protected async Task<Versioned<IEnumerable<Locality>>> SearchAsync( string country, string text, string? subdivision, int limit )
		{
			await CheckAuthorizationAsync( SecurityOperations.View ).ConfigureAwait( false );

			string code = CountryCode( country );
			string prefix = NameKey( text ) ?? throw new ArgumentException( "Search text is required.", nameof(text) );

			if( limit < 1 )
				throw new ArgumentOutOfRangeException( nameof(limit), limit, "The limit must be at least 1." );

			string? subdivisionCode = OptionalCode( subdivision );
			int capped = Math.Min( limit, MaximumSearchLimit );

			return await ReadVersionedAsync(
							code,
							( session, _ ) => session.SearchLocalitiesAsync( code, prefix, subdivisionCode, capped )
						)
						.ConfigureAwait( false );
		}
	}
}
