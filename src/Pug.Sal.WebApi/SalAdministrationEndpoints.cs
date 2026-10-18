using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Pug.Sal.Abstractions;
using Pug.Sal.Models;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.WebApi
{
	public static class SalAdministrationEndpoints
	{
		// The prefix of the ILocalityAdministration query options of an import: option.<name>=<value>
		// becomes ImportRequest.Options[<name>].
		public const string ImportOptionPrefix = "option.";

		/// <summary>
		/// Maps the <see cref="ILocalityAdministration"/> surface (DECISIONS D16, D20): full records,
		/// audit contexts included, under <see cref="SalApiOptions.RoutePrefix"/>,
		/// <c>administration/</c> by default. Pass <c>app</c> to mount at the application root, or a
		/// group from <c>app.MapGroup( "/prefix" )</c> to mount beneath a further prefix.
		/// Every endpoint requires an authenticated caller and, when <paramref name="configure"/> sets
		/// them, one of its scopes and its policy (D23); administration itself authorizes. Reads
		/// carry an ETag as the directory's do. An import posts the dataset itself as the request
		/// body (one of <see cref="ImportMediaTypes"/>), with the importer, source label and options
		/// as query parameters.
		/// Each endpoint carries OpenAPI metadata (name, tag, summary, request and response types);
		/// the host surfaces it, typically in development only, by configuring
		/// <c>AddEndpointsApiExplorer()</c> with <c>AddSwaggerGen()</c>, or
		/// <c>AddOpenApi()</c> with <c>MapOpenApi()</c>.
		/// </summary>
		/// <param name="routes">Where to map.</param>
		/// <param name="configure">Sets administration's route prefix, scopes and policy; null for the defaults.</param>
		public static IEndpointRouteBuilder MapSalAdministrationEndpoints( this IEndpointRouteBuilder routes,
																		Action<SalApiOptions>? configure = null ) =>
			routes.MapAdministrationEndpoints( SalAuthorization.Configure( SalRoutes.DefaultAdministrationPrefix, configure ) );

		internal static IEndpointRouteBuilder MapAdministrationEndpoints( this IEndpointRouteBuilder routes,
																			SalApiOptions options )
		{
			Func<RouteHandlerBuilder, RouteHandlerBuilder> authorize = SalAuthorization.Conventions( options );
			IEndpointRouteBuilder surface = SalAuthorization.Mount( routes, options );

			RouteHandlerBuilder Endpoint( RouteHandlerBuilder endpoint, string name, string summary ) =>
				authorize( endpoint )
					.WithName( name )
					.WithTags( "Sal administration" )
					.WithSummary( summary )
					.Produces( StatusCodes.Status403Forbidden );

			// == Reads ==

			Endpoint( surface.MapGet( "countries", GetCountries ), "SalAdminGetCountries",
					"List countries with their audit contexts, current ones unless includeRetired" )
				.Produces<IEnumerable<Country>>()
				.NotModified();

			Endpoint( surface.MapGet( "countries/versions", GetCountryVersions ), "SalAdminGetCountryVersions",
					"Get every country's current dataset version, for cache revalidation" )
				.Produces<IEnumerable<CountryVersion>>()
				.NotModified();

			Endpoint( surface.MapGet( "countries/{country}", GetCountry ), "SalAdminGetCountry",
					"Get a country with its audit contexts" )
				.Produces<Country>()
				.NotModified()
				.NotFound();

			Endpoint( surface.MapGet( "countries/{country}/subdivisions", GetSubdivisions ), "SalAdminGetSubdivisions",
					"List a country's subdivisions with their audit contexts, current ones unless includeRetired" )
				.Produces<Versioned<IEnumerable<Subdivision>>>()
				.NotModified()
				.NotFound();

			Endpoint( surface.MapGet( "countries/{country}/subdivisions/{subdivision}", GetSubdivision ), "SalAdminGetSubdivision",
					"Get a subdivision with its audit contexts" )
				.Produces<Versioned<Subdivision>>()
				.NotModified()
				.NotFound();

			Endpoint( surface.MapGet( "countries/{country}/localities/search", SearchLocalities ), "SalAdminSearchLocalities",
					"Search a country's current localities by name prefix, with their audit contexts" )
				.Produces<Versioned<IEnumerable<Locality>>>()
				.NotModified()
				.Invalid()
				.NotFound();

			Endpoint( surface.MapGet( "countries/{country}/localities", GetLocalities ), "SalAdminGetLocalities",
					"List a country's localities with their audit contexts, optionally by subdivision, postcode and name" )
				.Produces<Versioned<IEnumerable<Locality>>>()
				.NotModified()
				.NotFound();

			Endpoint( surface.MapGet( "countries/{country}/locality", GetLocality ), "SalAdminGetLocality",
					"Get a locality with its audit contexts by its key: subdivision, postcode and name" )
				.Produces<Versioned<Locality>>()
				.NotModified()
				.Invalid()
				.NotFound();

			Endpoint( surface.MapGet( "countries/{country}/imports", GetImportRuns ), "SalAdminGetImportRuns",
					"List a country's import runs, most recent first" )
				.Produces<IEnumerable<ImportRun>>()
				.NotFound();

			// == Countries ==

			Endpoint( surface.MapPost( "countries", CreateCountry ), "SalAdminCreateCountry", "Create a country" )
				.Produces<Country>( StatusCodes.Status201Created )
				.Invalid()
				.Duplicate();

			Endpoint( surface.MapPut( "countries/{country}", UpdateCountry ), "SalAdminUpdateCountry",
					"Replace a country's definition; the route names the country" )
				.Produces<Country>()
				.Invalid()
				.NotFound();

			Endpoint( surface.MapPost( "countries/{country}/retire", RetireCountry ), "SalAdminRetireCountry",
					"Retire a country; retiring a retired country changes nothing" )
				.Produces<Country>()
				.NotFound();

			Endpoint( surface.MapPost( "countries/{country}/reinstate", ReinstateCountry ), "SalAdminReinstateCountry",
					"Reinstate a retired country; reinstating a current country changes nothing" )
				.Produces<Country>()
				.NotFound();

			// == Subdivisions ==

			Endpoint( surface.MapPost( "countries/{country}/subdivisions", CreateSubdivision ), "SalAdminCreateSubdivision",
					"Create a subdivision; the route names the country" )
				.Produces<Versioned<Subdivision>>( StatusCodes.Status201Created )
				.Invalid()
				.NotFound()
				.Duplicate();

			Endpoint( surface.MapPut( "countries/{country}/subdivisions/{subdivision}", UpdateSubdivision ), "SalAdminUpdateSubdivision",
					"Replace a subdivision's name and type; the route names the subdivision" )
				.Produces<Versioned<Subdivision>>()
				.Invalid()
				.NotFound();

			Endpoint( surface.MapPost( "countries/{country}/subdivisions/{subdivision}/retire", RetireSubdivision ),
					"SalAdminRetireSubdivision", "Retire a subdivision; retiring a retired subdivision changes nothing" )
				.Produces<Versioned<Subdivision>>()
				.NotFound();

			Endpoint( surface.MapPost( "countries/{country}/subdivisions/{subdivision}/reinstate", ReinstateSubdivision ),
					"SalAdminReinstateSubdivision", "Reinstate a retired subdivision; reinstating a current subdivision changes nothing" )
				.Produces<Versioned<Subdivision>>()
				.NotFound();

			// == Localities ==

			Endpoint( surface.MapPost( "countries/{country}/localities", CreateLocality ), "SalAdminCreateLocality",
					"Create a locality; the route names the country" )
				.Produces<Versioned<Locality>>( StatusCodes.Status201Created )
				.Invalid()
				.NotFound()
				.Duplicate();

			Endpoint( surface.MapPost( "countries/{country}/locality/retire", RetireLocality ), "SalAdminRetireLocality",
					"Retire a locality, named by subdivision, postcode and name" )
				.Produces<Versioned<Locality>>()
				.Invalid()
				.NotFound();

			Endpoint( surface.MapPost( "countries/{country}/locality/reinstate", ReinstateLocality ), "SalAdminReinstateLocality",
					"Reinstate a retired locality, named by subdivision, postcode and name" )
				.Produces<Versioned<Locality>>()
				.Invalid()
				.NotFound();

			Endpoint( surface.MapDelete( "countries/{country}/locality", DeleteLocality ), "SalAdminDeleteLocality",
					"Delete a locality, named by subdivision, postcode and name; returns the dataset version produced" )
				.Produces<DatasetVersionResult>()
				.Invalid()
				.NotFound();

			// == Imports ==

			Endpoint( surface.MapPost( "countries/{country}/imports", Import ), "SalAdminImport",
					"Import a dataset covering the whole country" )
				.WithDescription( ImportDescription )
				.Accepts<Stream>( ImportMediaTypes[ 0 ], ImportMediaTypes[ 1.. ] )
				.Produces<ImportRun>()
				.Produces( StatusCodes.Status415UnsupportedMediaType )
				.Invalid()
				.NotFound()
				.Produces<FailureInfo>( StatusCodes.Status422UnprocessableEntity );

			return routes;
		}

		// The media types an import body may be sent as. Routing refuses others with 415, before
		// authentication runs.
		public static readonly string[] ImportMediaTypes = { "application/octet-stream", "text/csv", "text/plain" };

		// The option.<name> parameters are open-ended, so no handler parameter declares them.
		private const string ImportDescription =
			"The request body is the dataset itself, in the importer's format, sent as application/octet-stream, text/csv or " +
			"text/plain (any other media type is refused with 415). Query parameters: " +
			"importer (required) names a registered dataset importer, matched case-insensitively (e.g. csv); " +
			"source (optional) is a free-form label recorded on the import run; " +
			"option.<name>=<value> (any number) passes importer-specific options, each becoming ImportRequest.Options[<name>]. " +
			"The csv importer reads option.locality, option.postcode and option.subdivision (header names of those columns) " +
			"and option.delimiter (one character, or tab); subdivision values must be the registered codes, unchanged. " +
			"The dataset must cover the whole country: localities it does not hold are deleted. " +
			"422 reports an unknown importer (UNKNOWN_IMPORTER) or an unreadable or invalid dataset (INVALID_DATASET, with Position).";

		private static IResult Versioned<T>( HttpContext context, Versioned<T> read ) =>
			SalResults.Tagged( context, SalResults.VersionTag( read.DatasetVersion ), read );

		// == Reads ==

		private static Task<IResult> GetCountries( [FromQuery] bool? includeRetired, HttpContext context,
													[FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync(
				() => administration.GetCountriesAsync( includeRetired ?? false ),
				countries => SalResults.Tagged( context,
					SalResults.VersionsTag( countries.Select( c => ( c.Definition.Code, c.CurrentVersion ) ) ), countries ) );

		private static Task<IResult> GetCountryVersions( HttpContext context, [FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync(
				() => administration.GetCountryVersionsAsync(),
				versions => SalResults.Tagged( context, SalResults.VersionsTag( versions.Select( v => ( v.Country, v.Version ) ) ), versions ) );

		private static Task<IResult> GetCountry( [FromRoute] string country, HttpContext context,
												[FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync(
				() => administration.GetCountryAsync( country ),
				found => SalResults.Tagged( context, SalResults.VersionTag( found.CurrentVersion ), found ) );

		private static Task<IResult> GetSubdivisions( [FromRoute] string country, [FromQuery] bool? includeRetired, HttpContext context,
													[FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync( () => administration.GetSubdivisionsAsync( country, includeRetired ?? false ), read => Versioned( context, read ) );

		private static Task<IResult> GetSubdivision( [FromRoute] string country, [FromRoute] string subdivision, HttpContext context,
													[FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync( () => administration.GetSubdivisionAsync( country, subdivision ), read => Versioned( context, read ) );

		private static Task<IResult> GetLocalities( [FromRoute] string country, [FromQuery] string? subdivision, [FromQuery] string? postcode,
													[FromQuery] string? name, [FromQuery] bool? includeRetired, HttpContext context,
													[FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync(
				() => administration.GetLocalitiesAsync( country, subdivision!, postcode!, name!, includeRetired ?? false ),
				read => Versioned( context, read ) );

		private static async Task<IResult> GetLocality( [FromRoute] string country, [FromQuery] string? subdivision,
														[FromQuery] string? postcode, [FromQuery] string? name, HttpContext context,
														[FromServices] ILocalityAdministration administration )
		{
			if( string.IsNullOrWhiteSpace( name ) )
				return SalResults.MissingArgument( nameof(name) );

			return await SalResults.HandleAsync(
							() => administration.GetLocalityAsync( country, subdivision!, postcode!, name ),
							read => Versioned( context, read ) )
						.ConfigureAwait( false );
		}

		private static async Task<IResult> SearchLocalities( [FromRoute] string country, [FromQuery] string? text,
															[FromQuery] string? subdivision, [FromQuery] int? limit, HttpContext context,
															[FromServices] ILocalityAdministration administration )
		{
			if( string.IsNullOrWhiteSpace( text ) )
				return SalResults.MissingArgument( nameof(text) );

			return await SalResults.HandleAsync(
							() => administration.SearchLocalitiesAsync( country, text, subdivision!, limit ?? 20 ),
							read => Versioned( context, read ) )
						.ConfigureAwait( false );
		}

		private static Task<IResult> GetImportRuns( [FromRoute] string country, [FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync( () => administration.GetImportRunsAsync( country ), Results.Ok );

		// == Countries ==

		private static async Task<IResult> CreateCountry( [FromBody] CountryDefinition? definition,
														[FromServices] ILocalityAdministration administration,
														HttpContext http, [FromServices] LinkGenerator links )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync(
							() => administration.CreateCountryAsync( definition ),
							country => Results.Created(
								Location( http, links, "SalAdminGetCountry", new RouteValueDictionary { [ "country" ] = country.Definition.Code } ),
								country ) )
						.ConfigureAwait( false );
		}

		// The route names the country; the body's Code is ignored.
		private static async Task<IResult> UpdateCountry( [FromRoute] string country, [FromBody] CountryDefinition? definition,
														[FromServices] ILocalityAdministration administration )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync( () => administration.UpdateCountryAsync( definition with { Code = country } ), Results.Ok )
						.ConfigureAwait( false );
		}

		private static Task<IResult> RetireCountry( [FromRoute] string country, [FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync( () => administration.RetireCountryAsync( country ), Results.Ok );

		private static Task<IResult> ReinstateCountry( [FromRoute] string country, [FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync( () => administration.ReinstateCountryAsync( country ), Results.Ok );

		// == Subdivisions ==

		// The route names the country; the body's Country is ignored.
		private static async Task<IResult> CreateSubdivision( [FromRoute] string country, [FromBody] SubdivisionDefinition? definition,
															[FromServices] ILocalityAdministration administration,
															HttpContext http, [FromServices] LinkGenerator links )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync(
							() => administration.CreateSubdivisionAsync( definition with { Country = country } ),
							created => Results.Created(
								Location( http, links, "SalAdminGetSubdivision", new RouteValueDictionary
								{
									[ "country" ] = created.Value.Definition.Country, [ "subdivision" ] = created.Value.Definition.Code
								} ),
								created ) )
						.ConfigureAwait( false );
		}

		// The route names the subdivision; the body's Country and Code are ignored.
		private static async Task<IResult> UpdateSubdivision( [FromRoute] string country, [FromRoute] string subdivision,
															[FromBody] SubdivisionDefinition? definition,
															[FromServices] ILocalityAdministration administration )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync(
							() => administration.UpdateSubdivisionAsync( definition with { Country = country, Code = subdivision } ),
							Results.Ok )
						.ConfigureAwait( false );
		}

		private static Task<IResult> RetireSubdivision( [FromRoute] string country, [FromRoute] string subdivision,
														[FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync( () => administration.RetireSubdivisionAsync( country, subdivision ), Results.Ok );

		private static Task<IResult> ReinstateSubdivision( [FromRoute] string country, [FromRoute] string subdivision,
															[FromServices] ILocalityAdministration administration ) =>
			SalResults.HandleAsync( () => administration.ReinstateSubdivisionAsync( country, subdivision ), Results.Ok );

		// == Localities ==

		// The route names the country; the body's Country is ignored.
		private static async Task<IResult> CreateLocality( [FromRoute] string country, [FromBody] LocalityDefinition? definition,
															[FromServices] ILocalityAdministration administration,
															HttpContext http, [FromServices] LinkGenerator links )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync(
							() => administration.CreateLocalityAsync( definition with { Country = country } ),
							created => Results.Created( LocalityLocation( http, links, created.Value.Definition ), created ) )
						.ConfigureAwait( false );
		}

		// A locality is addressed by query; absent key parts are left out.
		private static string? LocalityLocation( HttpContext http, LinkGenerator links, LocalityDefinition locality )
		{
			RouteValueDictionary values = new() { [ "country" ] = locality.Country };

			foreach( ( string name, string? value ) in new[]
						{ ( "subdivision", locality.Subdivision ), ( "postcode", locality.Postcode ), ( "name", locality.Name ) } )
			{
				if( value is not null )
					values[ name ] = value;
			}

			return Location( http, links, "SalAdminGetLocality", values );
		}

		// The path of the named read endpoint, wherever the host mounted the surface (its prefix, any
		// enclosing group and the request's PathBase included).
		private static string? Location( HttpContext http, LinkGenerator links, string endpoint, RouteValueDictionary values ) =>
			links.GetPathByName( http, endpoint, values );

		private static async Task<IResult> RetireLocality( [FromRoute] string country, [FromQuery] string? subdivision,
															[FromQuery] string? postcode, [FromQuery] string? name,
															[FromServices] ILocalityAdministration administration )
		{
			if( string.IsNullOrWhiteSpace( name ) )
				return SalResults.MissingArgument( nameof(name) );

			return await SalResults.HandleAsync( () => administration.RetireLocalityAsync( country, subdivision!, postcode!, name ), Results.Ok )
						.ConfigureAwait( false );
		}

		private static async Task<IResult> ReinstateLocality( [FromRoute] string country, [FromQuery] string? subdivision,
															[FromQuery] string? postcode, [FromQuery] string? name,
															[FromServices] ILocalityAdministration administration )
		{
			if( string.IsNullOrWhiteSpace( name ) )
				return SalResults.MissingArgument( nameof(name) );

			return await SalResults.HandleAsync( () => administration.ReinstateLocalityAsync( country, subdivision!, postcode!, name ), Results.Ok )
						.ConfigureAwait( false );
		}

		private static async Task<IResult> DeleteLocality( [FromRoute] string country, [FromQuery] string? subdivision,
															[FromQuery] string? postcode, [FromQuery] string? name,
															[FromServices] ILocalityAdministration administration )
		{
			if( string.IsNullOrWhiteSpace( name ) )
				return SalResults.MissingArgument( nameof(name) );

			return await SalResults.HandleAsync(
							() => administration.DeleteLocalityAsync( country, subdivision!, postcode!, name ),
							version => Results.Ok( new DatasetVersionResult { DatasetVersion = version } ) )
						.ConfigureAwait( false );
		}

		// == Imports ==

		private static async Task<IResult> Import( [FromRoute] string country, [FromQuery] string? importer, [FromQuery] string? source,
													HttpRequest request, [FromServices] ILocalityAdministration administration )
		{
			if( string.IsNullOrWhiteSpace( importer ) )
				return SalResults.MissingArgument( nameof(importer) );

			Dictionary<string, string> options = request.Query
														.Where( q => q.Key.StartsWith( ImportOptionPrefix, StringComparison.OrdinalIgnoreCase ) &&
																	q.Key.Length > ImportOptionPrefix.Length )
														.ToDictionary( q => q.Key.Substring( ImportOptionPrefix.Length ), q => q.Value.ToString(),
																		StringComparer.OrdinalIgnoreCase );

			ImportRequest importRequest = new() { Importer = importer, Source = source!, Options = options };

			return await SalResults.HandleAsync( () => administration.ImportAsync( country, importRequest, request.Body ), Results.Ok )
						.ConfigureAwait( false );
		}
	}
}
