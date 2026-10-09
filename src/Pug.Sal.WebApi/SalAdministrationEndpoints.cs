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

		// Maps the ILocalityAdministration surface under sal/admin/ (DECISIONS D16): full records,
		// audit contexts included. Every endpoint requires an authenticated caller; administration
		// itself authorizes. Reads carry an ETag as the directory's do.
		//
		// An import posts the dataset itself as the request body (any media type), with the importer,
		// source label and options as query parameters.
		public static IEndpointRouteBuilder MapSalAdministrationEndpoints( this IEndpointRouteBuilder routes )
		{
			// == Reads ==

			Read( routes.MapGet( "sal/admin/countries", GetCountries ), "SalAdminGetCountries" ).Produces<IEnumerable<Country>>();
			Read( routes.MapGet( "sal/admin/countries/versions", GetCountryVersions ), "SalAdminGetCountryVersions" )
				.Produces<IEnumerable<CountryVersion>>();
			Read( routes.MapGet( "sal/admin/countries/{country}", GetCountry ), "SalAdminGetCountry" ).Produces<Country>();
			Read( routes.MapGet( "sal/admin/countries/{country}/subdivisions", GetSubdivisions ), "SalAdminGetSubdivisions" )
				.Produces<Versioned<IEnumerable<Subdivision>>>();
			Read( routes.MapGet( "sal/admin/countries/{country}/subdivisions/{subdivision}", GetSubdivision ), "SalAdminGetSubdivision" )
				.Produces<Versioned<Subdivision>>();
			Read( routes.MapGet( "sal/admin/countries/{country}/localities/search", SearchLocalities ), "SalAdminSearchLocalities" )
				.Produces<Versioned<IEnumerable<Locality>>>();
			Read( routes.MapGet( "sal/admin/countries/{country}/localities", GetLocalities ), "SalAdminGetLocalities" )
				.Produces<Versioned<IEnumerable<Locality>>>();
			Read( routes.MapGet( "sal/admin/countries/{country}/locality", GetLocality ), "SalAdminGetLocality" )
				.Produces<Versioned<Locality>>();
			Write( routes.MapGet( "sal/admin/countries/{country}/imports", GetImportRuns ), "SalAdminGetImportRuns" )
				.Produces<IEnumerable<ImportRun>>();

			// == Countries ==

			Write( routes.MapPost( "sal/admin/countries", CreateCountry ), "SalAdminCreateCountry" )
				.Produces<Country>( StatusCodes.Status201Created );
			Write( routes.MapPut( "sal/admin/countries/{country}", UpdateCountry ), "SalAdminUpdateCountry" ).Produces<Country>();
			Write( routes.MapPost( "sal/admin/countries/{country}/retire", RetireCountry ), "SalAdminRetireCountry" ).Produces<Country>();
			Write( routes.MapPost( "sal/admin/countries/{country}/reinstate", ReinstateCountry ), "SalAdminReinstateCountry" )
				.Produces<Country>();

			// == Subdivisions ==

			Write( routes.MapPost( "sal/admin/countries/{country}/subdivisions", CreateSubdivision ), "SalAdminCreateSubdivision" )
				.Produces<Versioned<Subdivision>>( StatusCodes.Status201Created );
			Write( routes.MapPut( "sal/admin/countries/{country}/subdivisions/{subdivision}", UpdateSubdivision ), "SalAdminUpdateSubdivision" )
				.Produces<Versioned<Subdivision>>();
			Write( routes.MapPost( "sal/admin/countries/{country}/subdivisions/{subdivision}/retire", RetireSubdivision ),
					"SalAdminRetireSubdivision" )
				.Produces<Versioned<Subdivision>>();
			Write( routes.MapPost( "sal/admin/countries/{country}/subdivisions/{subdivision}/reinstate", ReinstateSubdivision ),
					"SalAdminReinstateSubdivision" )
				.Produces<Versioned<Subdivision>>();

			// == Localities ==

			Write( routes.MapPost( "sal/admin/countries/{country}/localities", CreateLocality ), "SalAdminCreateLocality" )
				.Produces<Versioned<Locality>>( StatusCodes.Status201Created );
			Write( routes.MapPost( "sal/admin/countries/{country}/locality/retire", RetireLocality ), "SalAdminRetireLocality" )
				.Produces<Versioned<Locality>>();
			Write( routes.MapPost( "sal/admin/countries/{country}/locality/reinstate", ReinstateLocality ), "SalAdminReinstateLocality" )
				.Produces<Versioned<Locality>>();
			Write( routes.MapDelete( "sal/admin/countries/{country}/locality", DeleteLocality ), "SalAdminDeleteLocality" )
				.Produces<DatasetVersionResult>();

			// == Imports ==

			Write( routes.MapPost( "sal/admin/countries/{country}/imports", Import ), "SalAdminImport" )
				.Accepts<Stream>( "application/octet-stream", "text/csv", "text/plain" )
				.Produces<ImportRun>()
				.Produces<FailureInfo>( StatusCodes.Status422UnprocessableEntity );

			return routes;
		}

		private static RouteHandlerBuilder Read( RouteHandlerBuilder endpoint, string name ) =>
			Write( endpoint, name ).Produces( StatusCodes.Status304NotModified );

		private static RouteHandlerBuilder Write( RouteHandlerBuilder endpoint, string name ) =>
			endpoint.RequireAuthorization()
					.WithName( name )
					.WithTags( "Sal administration" )
					.Produces<BadRequestResultInfo>( StatusCodes.Status400BadRequest )
					.Produces( StatusCodes.Status403Forbidden )
					.Produces<FailureInfo>( StatusCodes.Status404NotFound )
					.Produces<FailureInfo>( StatusCodes.Status409Conflict );

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
														[FromServices] ILocalityAdministration administration )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync(
							() => administration.CreateCountryAsync( definition ),
							country => Results.Created( $"sal/admin/countries/{Uri.EscapeDataString( country.Definition.Code )}", country ) )
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
															[FromServices] ILocalityAdministration administration )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync(
							() => administration.CreateSubdivisionAsync( definition with { Country = country } ),
							created => Results.Created(
								$"sal/admin/countries/{Uri.EscapeDataString( created.Value.Definition.Country )}/subdivisions/{Uri.EscapeDataString( created.Value.Definition.Code )}",
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
															[FromServices] ILocalityAdministration administration )
		{
			if( definition is null )
				return SalResults.MissingArgument( nameof(definition) );

			return await SalResults.HandleAsync(
							() => administration.CreateLocalityAsync( definition with { Country = country } ),
							created => Results.Created( LocalityLocation( created.Value.Definition ), created ) )
						.ConfigureAwait( false );
		}

		private static string LocalityLocation( LocalityDefinition locality )
		{
			string query = string.Join( "&",
				new[] { ( "subdivision", locality.Subdivision ), ( "postcode", locality.Postcode ), ( "name", locality.Name ) }
					.Where( p => p.Item2 is not null )
					.Select( p => $"{p.Item1}={Uri.EscapeDataString( p.Item2 )}" ) );

			return $"sal/admin/countries/{Uri.EscapeDataString( locality.Country )}/locality?{query}";
		}

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
