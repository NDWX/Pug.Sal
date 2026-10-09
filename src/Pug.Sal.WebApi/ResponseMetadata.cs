using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.WebApi
{
	// The failure responses an endpoint can actually produce, declared for OpenAPI (D20); see
	// SalResults for the mapping behind each.
	internal static class ResponseMetadata
	{
		// An If-None-Match read whose ETag still matches.
		public static RouteHandlerBuilder NotModified( this RouteHandlerBuilder endpoint ) =>
			endpoint.Produces( StatusCodes.Status304NotModified );

		// ArgumentException, or a required argument missing.
		public static RouteHandlerBuilder Invalid( this RouteHandlerBuilder endpoint ) =>
			endpoint.Produces<BadRequestResultInfo>( StatusCodes.Status400BadRequest );

		// An unknown country, subdivision or locality.
		public static RouteHandlerBuilder NotFound( this RouteHandlerBuilder endpoint ) =>
			endpoint.Produces<FailureInfo>( StatusCodes.Status404NotFound );

		// DuplicateEntryException.
		public static RouteHandlerBuilder Duplicate( this RouteHandlerBuilder endpoint ) =>
			endpoint.Produces<FailureInfo>( StatusCodes.Status409Conflict );
	}
}
