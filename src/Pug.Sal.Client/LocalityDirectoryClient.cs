using System.Globalization;
using Pug.HttpApiClient;
using Pug.HttpApiClient.Json;
using Pug.Sal.Abstractions;
using Pug.Sal.Models;
using Pug.Sal.WebApi.Models;
using static Pug.Sal.Client.SalPaths;

namespace Pug.Sal.Client
{
	// ILocalityDirectory over the routes MapSalDirectoryEndpoints() maps (DECISIONS D16). Bind it in
	// place of the in-process LocalityDirectory: the contract, including which exceptions surface,
	// is the same. The IHttpApiClient carries the caller's credentials; authorization stays with
	// the server's facade.
	public class LocalityDirectoryClient : ApiClient, ILocalityDirectory
	{
		private readonly string _root;

		// routePrefix: where the host mounted the directory (SalApiOptions.RoutePrefix), relative to
		// the IHttpApiClient's base address.
		public LocalityDirectoryClient( IHttpApiClient httpClient, string routePrefix = SalRoutes.DefaultDirectoryPrefix )
			: base( httpClient )
		{
			_root = Root( routePrefix );
		}

		public Task<IEnumerable<CountryInfo>> GetCountriesAsync( bool includeRetired = false ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<IEnumerable<CountryInfo>>(
				$"{_root}countries", queries: Queries( ( "includeRetired", Flag( includeRetired ) ) ) ) );

		public Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync() =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<IEnumerable<CountryVersion>>( $"{_root}countries/versions" ) );

		public Task<CountryInfo> GetCountryAsync( string country ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<CountryInfo>( Country( _root, country ) ) );

		public Task<Versioned<IEnumerable<SubdivisionInfo>>> GetSubdivisionsAsync( string country, bool includeRetired = false ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<IEnumerable<SubdivisionInfo>>>(
				$"{Country( _root, country )}/subdivisions", queries: Queries( ( "includeRetired", Flag( includeRetired ) ) ) ) );

		public Task<Versioned<SubdivisionInfo>> GetSubdivisionAsync( string country, string subdivision ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<SubdivisionInfo>>(
				$"{Country( _root, country )}/subdivisions/{Escape( subdivision )}" ) );

		public Task<Versioned<IEnumerable<LocalityInfo>>> GetLocalitiesAsync( string country, string subdivision = null!, string postcode = null!,
																			string name = null!, bool includeRetired = false ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<IEnumerable<LocalityInfo>>>(
				$"{Country( _root, country )}/localities",
				queries: Queries( ( "subdivision", subdivision ), ( "postcode", postcode ), ( "name", name ),
								( "includeRetired", Flag( includeRetired ) ) ) ) );

		public Task<Versioned<LocalityInfo>> GetLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<LocalityInfo>>(
				$"{Country( _root, country )}/locality", queries: LocalityKey( subdivision, postcode, name ) ) );

		public Task<Versioned<IEnumerable<LocalityInfo>>> SearchLocalitiesAsync( string country, string text, string subdivision = null!,
																				int limit = 20 ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<IEnumerable<LocalityInfo>>>(
				$"{Country( _root, country )}/localities/search",
				queries: Queries( ( "text", text ), ( "subdivision", subdivision ),
								( "limit", limit.ToString( CultureInfo.InvariantCulture ) ) ) ) );

		public Task<ValidationResult> ValidateAsync( AddressCandidate candidate ) =>
			ApiFailures.TranslateAsync( () => HttpClient.PostAsync<AddressCandidate, ValidationResult>( $"{_root}validate", candidate ) );
	}
}
