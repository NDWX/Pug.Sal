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
		// Maps the ILocalityDirectory surface under sal/ (DECISIONS D16): audit-free records. Every
		// endpoint requires an authenticated caller; the directory itself authorizes. Reads carry an
		// ETag derived from the dataset version(s) they were read at and answer If-None-Match with
		// 304. A locality is addressed by query (subdivision, postcode, name), since its
		// subdivision and postcode may be absent.
		public static IEndpointRouteBuilder MapSalDirectoryEndpoints( this IEndpointRouteBuilder routes )
		{
			routes.MapGet( "sal/countries", GetCountries )
				.RequireAuthorization()
				.WithName( "SalGetCountries" )
				.WithTags( "Sal" )
				.Produces<IEnumerable<CountryInfo>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden );

			routes.MapGet( "sal/countries/versions", GetCountryVersions )
				.RequireAuthorization()
				.WithName( "SalGetCountryVersions" )
				.WithTags( "Sal" )
				.Produces<IEnumerable<CountryVersion>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden );

			routes.MapGet( "sal/countries/{country}", GetCountry )
				.RequireAuthorization()
				.WithName( "SalGetCountry" )
				.WithTags( "Sal" )
				.Produces<CountryInfo>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/subdivisions", GetSubdivisions )
				.RequireAuthorization()
				.WithName( "SalGetSubdivisions" )
				.WithTags( "Sal" )
				.Produces<Versioned<IEnumerable<SubdivisionInfo>>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/subdivisions/{subdivision}", GetSubdivision )
				.RequireAuthorization()
				.WithName( "SalGetSubdivision" )
				.WithTags( "Sal" )
				.Produces<Versioned<SubdivisionInfo>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/localities/search", SearchLocalities )
				.RequireAuthorization()
				.WithName( "SalSearchLocalities" )
				.WithTags( "Sal" )
				.Produces<Versioned<IEnumerable<LocalityInfo>>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces<BadRequestResultInfo>( StatusCodes.Status400BadRequest )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/localities", GetLocalities )
				.RequireAuthorization()
				.WithName( "SalGetLocalities" )
				.WithTags( "Sal" )
				.Produces<Versioned<IEnumerable<LocalityInfo>>>()
				.Produces( StatusCodes.Status304NotModified )
				.Produces( StatusCodes.Status403Forbidden )
				.Produces<FailureInfo>( StatusCodes.Status404NotFound );

			routes.MapGet( "sal/countries/{country}/locality", GetLocality )
				.RequireAuthorization()
				.WithName( "SalGetLocality" )
				.WithTags( "Sal" )
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
