using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Pug.Sal.Abstractions;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.WebApi
{
	// Turns facade outcomes into HTTP results. The facades authorize and validate; the endpoints
	// only translate (DECISIONS D8):
	// - AccessDeniedException -> 403 (only that type: any other UnauthorizedAccessException is a
	//   server fault, D25);
	// - Unknown{Country,Subdivision,Locality}Exception -> 404 with a FailureInfo;
	// - DuplicateEntryException -> 409 with a FailureInfo;
	// - UnknownDatasetImporterException, DatasetImportException -> 422 with a FailureInfo;
	// - ArgumentException -> 400 with a BadRequestResultInfo.
	internal static class SalResults
	{
		public static async Task<IResult> HandleAsync<T>( Func<Task<T>> call, Func<T, IResult> success )
		{
			try
			{
				return success( await call().ConfigureAwait( false ) );
			}
			catch( AccessDeniedException )
			{
				return Results.Forbid();
			}
			catch( UnknownCountryException exception )
			{
				return Results.NotFound( Failure( FailureTypes.UnknownCountry, exception ) with { Country = exception.Country } );
			}
			catch( UnknownSubdivisionException exception )
			{
				return Results.NotFound( Failure( FailureTypes.UnknownSubdivision, exception ) with
				{
					Country = exception.Country, Subdivision = exception.Subdivision
				} );
			}
			catch( UnknownLocalityException exception )
			{
				return Results.NotFound( Failure( FailureTypes.UnknownLocality, exception ) with
				{
					Country = exception.Country, Subdivision = exception.Subdivision, Postcode = exception.Postcode, Name = exception.Name
				} );
			}
			catch( DuplicateEntryException exception )
			{
				return Results.Conflict( Failure( FailureTypes.DuplicateEntry, exception ) with
				{
					EntityType = exception.EntityType, Key = exception.Key
				} );
			}
			catch( UnknownDatasetImporterException exception )
			{
				return Results.UnprocessableEntity( Failure( FailureTypes.UnknownImporter, exception ) with { Importer = exception.Importer } );
			}
			catch( DatasetImportException exception )
			{
				return Results.UnprocessableEntity( Failure( FailureTypes.InvalidDataset, exception ) with { Position = exception.Position } );
			}
			catch( ArgumentException exception )
			{
				return Results.BadRequest( new BadRequestResultInfo( "INVALID_ARGUMENT", exception.ParamName ?? string.Empty, exception.Message ) );
			}
		}

		public static IResult MissingArgument( string argument ) =>
			Results.BadRequest( new BadRequestResultInfo( "MISSING_ARGUMENT", argument, $"{argument} is required." ) );

		// A read answered with an ETag: 304 when the request's If-None-Match already names it
		// (DECISIONS D6). The tag changes whenever the country's dataset version does.
		public static IResult Tagged( HttpContext context, string tag, object value )
		{
			string etag = $"\"{tag}\"";

			context.Response.Headers.ETag = etag;

			foreach( string? candidate in context.Request.Headers.IfNoneMatch )
			{
				if( candidate is null )
					continue;

				foreach( string part in candidate.Split( ',' ) )
				{
					string trimmed = part.Trim();

					if( trimmed == "*" || trimmed == etag || trimmed == "W/" + etag )
						return Results.StatusCode( StatusCodes.Status304NotModified );
				}
			}

			return Results.Ok( value );
		}

		public static string VersionTag( DateTime version ) => version.Ticks.ToString( CultureInfo.InvariantCulture );

		// A tag over several countries' versions (listings across countries).
		public static string VersionsTag( IEnumerable<(string Country, DateTime Version)> versions )
		{
			string text = string.Join( ";", versions.Select( v => $"{v.Country}:{v.Version.Ticks}" ) );

			return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ), 0, 16 );
		}

		private static FailureInfo Failure( string type, Exception exception ) => new() { Type = type, Message = exception.Message };
	}
}
