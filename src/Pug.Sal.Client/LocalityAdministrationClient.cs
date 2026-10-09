using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Pug.HttpApiClient;
using Pug.HttpApiClient.Json;
using Pug.Sal.Abstractions;
using Pug.Sal.Models;
using Pug.Sal.WebApi.Models;
using static Pug.Sal.Client.SalPaths;

namespace Pug.Sal.Client
{
	// ILocalityAdministration over the routes MapSalAdministrationEndpoints() maps (DECISIONS D16).
	// Bind it in place of the in-process LocalityAdministration: the contract, including which
	// exceptions surface, is the same. An import streams the dataset as the request body.
	public class LocalityAdministrationClient : ApiClient, ILocalityAdministration
	{
		private const string Root = "sal/admin";

		// The prefix the endpoints read import options from; see SalAdministrationEndpoints.
		private const string ImportOptionPrefix = "option.";

		private static readonly MediaTypeWithQualityHeaderValue Json = new( "application/json" );

		public LocalityAdministrationClient( IHttpApiClient httpClient )
			: base( httpClient )
		{
		}

		// == Reads ==

		public Task<IEnumerable<Country>> GetCountriesAsync( bool includeRetired = false ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<IEnumerable<Country>>(
				$"{Root}/countries", queries: Queries( ( "includeRetired", Flag( includeRetired ) ) ) ) );

		public Task<IEnumerable<CountryVersion>> GetCountryVersionsAsync() =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<IEnumerable<CountryVersion>>( $"{Root}/countries/versions" ) );

		public Task<Country> GetCountryAsync( string country ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Country>( Country( Root, country ) ) );

		public Task<Versioned<IEnumerable<Subdivision>>> GetSubdivisionsAsync( string country, bool includeRetired = false ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<IEnumerable<Subdivision>>>(
				$"{Country( Root, country )}/subdivisions", queries: Queries( ( "includeRetired", Flag( includeRetired ) ) ) ) );

		public Task<Versioned<Subdivision>> GetSubdivisionAsync( string country, string subdivision ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<Subdivision>>(
				$"{Country( Root, country )}/subdivisions/{Escape( subdivision )}" ) );

		public Task<Versioned<IEnumerable<Locality>>> GetLocalitiesAsync( string country, string subdivision = null!, string postcode = null!,
																		string name = null!, bool includeRetired = false ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<IEnumerable<Locality>>>(
				$"{Country( Root, country )}/localities",
				queries: Queries( ( "subdivision", subdivision ), ( "postcode", postcode ), ( "name", name ),
								( "includeRetired", Flag( includeRetired ) ) ) ) );

		public Task<Versioned<Locality>> GetLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<Locality>>(
				$"{Country( Root, country )}/locality", queries: LocalityKey( subdivision, postcode, name ) ) );

		public Task<Versioned<IEnumerable<Locality>>> SearchLocalitiesAsync( string country, string text, string subdivision = null!,
																			int limit = 20 ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<Versioned<IEnumerable<Locality>>>(
				$"{Country( Root, country )}/localities/search",
				queries: Queries( ( "text", text ), ( "subdivision", subdivision ),
								( "limit", limit.ToString( CultureInfo.InvariantCulture ) ) ) ) );

		// == Countries ==

		public Task<Country> CreateCountryAsync( CountryDefinition definition ) =>
			ApiFailures.TranslateAsync( () => HttpClient.PostAsync<CountryDefinition, Country>( $"{Root}/countries", definition ) );

		public Task<Country> UpdateCountryAsync( CountryDefinition definition ) =>
			ApiFailures.TranslateAsync( () => HttpClient.PutAsync<CountryDefinition, Country>(
				Country( Root, definition?.Code ?? string.Empty ), definition! ) );

		public Task<Country> RetireCountryAsync( string country ) => PostAsync<Country>( $"{Country( Root, country )}/retire" );

		public Task<Country> ReinstateCountryAsync( string country ) => PostAsync<Country>( $"{Country( Root, country )}/reinstate" );

		// == Subdivisions ==

		public Task<Versioned<Subdivision>> CreateSubdivisionAsync( SubdivisionDefinition definition ) =>
			ApiFailures.TranslateAsync( () => HttpClient.PostAsync<SubdivisionDefinition, Versioned<Subdivision>>(
				$"{Country( Root, definition?.Country ?? string.Empty )}/subdivisions", definition! ) );

		public Task<Versioned<Subdivision>> UpdateSubdivisionAsync( SubdivisionDefinition definition ) =>
			ApiFailures.TranslateAsync( () => HttpClient.PutAsync<SubdivisionDefinition, Versioned<Subdivision>>(
				$"{Country( Root, definition?.Country ?? string.Empty )}/subdivisions/{Escape( definition?.Code ?? string.Empty )}",
				definition! ) );

		public Task<Versioned<Subdivision>> RetireSubdivisionAsync( string country, string subdivision ) =>
			PostAsync<Versioned<Subdivision>>( $"{Country( Root, country )}/subdivisions/{Escape( subdivision )}/retire" );

		public Task<Versioned<Subdivision>> ReinstateSubdivisionAsync( string country, string subdivision ) =>
			PostAsync<Versioned<Subdivision>>( $"{Country( Root, country )}/subdivisions/{Escape( subdivision )}/reinstate" );

		// == Localities ==

		public Task<Versioned<Locality>> CreateLocalityAsync( LocalityDefinition definition ) =>
			ApiFailures.TranslateAsync( () => HttpClient.PostAsync<LocalityDefinition, Versioned<Locality>>(
				$"{Country( Root, definition?.Country ?? string.Empty )}/localities", definition! ) );

		public Task<Versioned<Locality>> RetireLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			PostAsync<Versioned<Locality>>( $"{Country( Root, country )}/locality/retire", LocalityKey( subdivision, postcode, name ) );

		public Task<Versioned<Locality>> ReinstateLocalityAsync( string country, string subdivision, string postcode, string name ) =>
			PostAsync<Versioned<Locality>>( $"{Country( Root, country )}/locality/reinstate", LocalityKey( subdivision, postcode, name ) );

		public async Task<DateTime> DeleteLocalityAsync( string country, string subdivision, string postcode, string name )
		{
			DatasetVersionResult result = await ApiFailures.TranslateAsync(
												async () =>
												{
													using HttpResponseMessage response = await HttpClient.DeleteAsync(
																							$"{Country( Root, country )}/locality",
																							queries: LocalityKey( subdivision, postcode, name ),
																							mediaType: Json )
																						.ConfigureAwait( false );

													return await ReadAsync<DatasetVersionResult>( response ).ConfigureAwait( false );
												} )
											.ConfigureAwait( false );

			return result.DatasetVersion;
		}

		// == Imports ==

		public Task<ImportRun> ImportAsync( string country, ImportRequest request, Stream source )
		{
			if( request is null )
				throw new ArgumentNullException( nameof(request) );

			if( source is null )
				throw new ArgumentNullException( nameof(source) );

			List<(string Name, string? Value)> queries = new() { ( "importer", request.Importer ), ( "source", request.Source ) };

			if( request.Options is not null )
				queries.AddRange( request.Options.Select( option => ( ImportOptionPrefix + option.Key, (string?)option.Value ) ) );

			return ApiFailures.TranslateAsync(
				async () =>
				{
					StreamContent content = new( source );
					content.Headers.ContentType = new MediaTypeHeaderValue( "application/octet-stream" );

					using HttpResponseMessage response = await HttpClient.PostAsync(
																$"{Country( Root, country )}/imports", content, Json,
																queries: Queries( queries.ToArray() ) )
															.ConfigureAwait( false );

					return await ReadAsync<ImportRun>( response ).ConfigureAwait( false );
				} );
		}

		public Task<IEnumerable<ImportRun>> GetImportRunsAsync( string country ) =>
			ApiFailures.TranslateAsync( () => HttpClient.GetAsync<IEnumerable<ImportRun>>( $"{Country( Root, country )}/imports" ) );

		// == Plumbing ==

		// The retire/reinstate actions take no request body; an empty JSON object keeps the request
		// well-formed for the media type.
		private Task<T> PostAsync<T>( string path, IDictionary<string, string>? queries = null ) =>
			ApiFailures.TranslateAsync( () => HttpClient.PostAsync<object, T>( path, new object(), queries: queries ) );

		// Pug.HttpApiClient's raw calls throw only for the statuses it knows (not 422), so the status
		// is checked here as its JSON helpers check it.
		private static async Task<T> ReadAsync<T>( HttpResponseMessage response )
		{
			if( !response.IsSuccessStatusCode )
				throw new HttpApiRequestException( response );

			string body = await response.Content.ReadAsStringAsync().ConfigureAwait( false );

			return JsonSerializer.Deserialize<T>( body, ApiFailures.JsonOptions ) ??
					throw new InvalidOperationException( $"The response body did not deserialize to {typeof(T).Name}." );
		}
	}
}
