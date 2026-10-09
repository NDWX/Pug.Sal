using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Pug.Sal.Abstractions;
using Pug.Sal.Models;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.WebApi
{
	public static class SalDirectoryEndpoints
	{
		/// <summary>
		/// Maps the <see cref="ILocalityDirectory"/> surface under <c>sal/</c> (DECISIONS D16, D20):
		/// audit-free records. Pass <c>app</c> to mount at the application root, or a group from
		/// <c>app.MapGroup( "/prefix" )</c> to mount beneath a further prefix.
		/// Every endpoint requires an authenticated caller; the directory itself authorizes. Reads
		/// carry an ETag derived from the dataset version(s) they were read at and answer
		/// If-None-Match with 304. A locality is addressed by query (subdivision, postcode, name),
		/// since its subdivision and postcode may be absent.
		/// Each endpoint carries OpenAPI metadata (name, tag, summary, request and response types);
		/// the host surfaces it, typically in development only, by configuring
		/// <c>AddEndpointsApiExplorer()</c> with <c>AddSwaggerGen()</c>, or
		/// <c>AddOpenApi()</c> with <c>MapOpenApi()</c>.
		/// </summary>
		public static IEndpointRouteBuilder MapSalDirectoryEndpoints( this IEndpointRouteBuilder routes )
		{
			routes.MapGet( "sal/countries", GetCountries )
				.RequireAuthorization()
				.WithName( "SalGetCountries" )
				.WithTags( "Sal" )
				.WithSummary( "List countries, current ones unless includeRetired" )
				.Produces<IEnumerable<CountryInfo>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden );

			routes.MapGet( "sal/countries/versions", GetCountryVersions )
				.RequireAuthorization()
				.WithName( "SalGetCountryVersions" )
				.WithTags( "Sal" )
				.WithSummary( "Get every country's current dataset version, for cache revalidation" )
				.Produces<IEnumerable<CountryVersion>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden );

			routes.MapGet( "sal/countries/{country}", GetCountry )
				.RequireAuthorization()
				.WithName( "SalGetCountry" )
				.WithTags( "Sal" )
				.WithSummary( "Get a country by ISO 3166-1 alpha-2 code" )
				.Produces<CountryInfo>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/subdivisions", GetSubdivisions )
				.RequireAuthorization()
				.WithName( "SalGetSubdivisions" )
				.WithTags( "Sal" )
				.WithSummary( "List a country's subdivisions, current ones unless includeRetired" )
				.Produces<Versioned<IEnumerable<SubdivisionInfo>>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/subdivisions/{subdivision}", GetSubdivision )
				.RequireAuthorization()
				.WithName( "SalGetSubdivision" )
				.WithTags( "Sal" )
				.WithSummary( "Get a subdivision by code" )
				.Produces<Versioned<SubdivisionInfo>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/localities/search", SearchLocalities )
				.RequireAuthorization()
				.WithName( "SalSearchLocalities" )
				.WithTags( "Sal" )
				.WithSummary( "Search a country's current localities by name prefix" )
				.Produces<Versioned<IEnumerable<LocalityInfo>>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces<BadRequestResultInfo>( StatusCodes.Status400BadRequest )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/localities", GetLocalities )
				.RequireAuthorization()
				.WithName( "SalGetLocalities" )
				.WithTags( "Sal" )
				.WithSummary( "List a country's localities, optionally by subdivision, postcode and name" )
				.Produces<Versioned<IEnumerable<LocalityInfo>>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/locality", GetLocality )
				.RequireAuthorization()
				.WithName( "SalGetLocality" )
				.WithTags( "Sal" )
				.WithSummary( "Get a locality by its key: subdivision, postcode and name" )
				.Produces<Versioned<LocalityInfo>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces<BadRequestResultInfo>( StatusCodes.Status400BadRequest )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			// POST because the candidate travels as a body; it changes nothing.
			routes.MapPost( "sal/validate", Validate )
				.RequireAuthorization()
				.WithName( "SalValidate" )
				.WithTags( "Sal" )
				.WithSummary( "Validate an address candidate's country, subdivision, locality and postcode" )
				.Produces<ValidationResult>()
				.Produces<BadRequestResultInfo>( StatusCodes.Status400BadRequest )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			return routes;
		}

		private static Task<IResult> GetCountries( [FromQuery] bool? includeRetired, HttpContext context,
													[FromServices] ILocalityDirectory directory ) =>
			SalResults.HandleAsync(
				() => directory.GetCountriesAsync( includeRetired ?? false ),
				countries => SalResults.Tagged( context,
					SalResults.VersionsTag( countries.Select( c => ( c.Definition.Code, c.CurrentVersion ) ) ), countries ) );

		private static Task<IResult> GetCountryVersions( HttpContext context, [FromServices] ILocalityDirectory directory ) =>
			SalResults.HandleAsync(
				() => directory.GetCountryVersionsAsync(),
				versions => SalResults.Tagged( context, SalResults.VersionsTag( versions.Select( v => ( v.Country, v.Version ) ) ), versions ) );

		private static Task<IResult> GetCountry( [FromRoute] string country, HttpContext context,
												[FromServices] ILocalityDirectory directory ) =>
			SalResults.HandleAsync(
				() => directory.GetCountryAsync( country ),
				found => SalResults.Tagged( context, SalResults.VersionTag( found.CurrentVersion ), found ) );

		private static Task<IResult> GetSubdivisions( [FromRoute] string country, [FromQuery] bool? includeRetired, HttpContext context,
													[FromServices] ILocalityDirectory directory ) =>
			SalResults.HandleAsync(
				() => directory.GetSubdivisionsAsync( country, includeRetired ?? false ),
				read => SalResults.Tagged( context, SalResults.VersionTag( read.DatasetVersion ), read ) );

		private static Task<IResult> GetSubdivision( [FromRoute] string country, [FromRoute] string subdivision, HttpContext context,
													[FromServices] ILocalityDirectory directory ) =>
			SalResults.HandleAsync(
				() => directory.GetSubdivisionAsync( country, subdivision ),
				read => SalResults.Tagged( context, SalResults.VersionTag( read.DatasetVersion ), read ) );

		private static Task<IResult> GetLocalities( [FromRoute] string country, [FromQuery] string? subdivision, [FromQuery] string? postcode,
													[FromQuery] string? name, [FromQuery] bool? includeRetired, HttpContext context,
													[FromServices] ILocalityDirectory directory ) =>
			SalResults.HandleAsync(
				() => directory.GetLocalitiesAsync( country, subdivision!, postcode!, name!, includeRetired ?? false ),
				read => SalResults.Tagged( context, SalResults.VersionTag( read.DatasetVersion ), read ) );

		private static async Task<IResult> GetLocality( [FromRoute] string country, [FromQuery] string? subdivision,
														[FromQuery] string? postcode, [FromQuery] string? name, HttpContext context,
														[FromServices] ILocalityDirectory directory )
		{
			if( string.IsNullOrWhiteSpace( name ) )
				return SalResults.MissingArgument( nameof(name) );

			return await SalResults.HandleAsync(
							() => directory.GetLocalityAsync( country, subdivision!, postcode!, name ),
							read => SalResults.Tagged( context, SalResults.VersionTag( read.DatasetVersion ), read ) )
						.ConfigureAwait( false );
		}

		private static async Task<IResult> SearchLocalities( [FromRoute] string country, [FromQuery] string? text,
															[FromQuery] string? subdivision, [FromQuery] int? limit, HttpContext context,
															[FromServices] ILocalityDirectory directory )
		{
			if( string.IsNullOrWhiteSpace( text ) )
				return SalResults.MissingArgument( nameof(text) );

			return await SalResults.HandleAsync(
							() => directory.SearchLocalitiesAsync( country, text, subdivision!, limit ?? 20 ),
							read => SalResults.Tagged( context, SalResults.VersionTag( read.DatasetVersion ), read ) )
						.ConfigureAwait( false );
		}

		private static async Task<IResult> Validate( [FromBody] AddressCandidate? candidate, [FromServices] ILocalityDirectory directory )
		{
			if( candidate is null )
				return SalResults.MissingArgument( nameof(candidate) );

			return await SalResults.HandleAsync( () => directory.ValidateAsync( candidate ), Results.Ok ).ConfigureAwait( false );
		}
	}
}
