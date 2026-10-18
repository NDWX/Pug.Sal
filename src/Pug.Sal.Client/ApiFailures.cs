using System.Net;
using System.Text.Json;
using Pug.HttpApiClient;
using Pug.Sal.Abstractions;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.Client
{
	// Turns the transport's failures back into the exceptions the Sal facades document, so a caller
	// cannot tell the HTTP clients from the in-process facades: 403 -> AccessDeniedException (without an Operation: a 403 carries no body); 404, 409 (DuplicateEntryException, CountryConfigurationException) and
	// 422 -> the exception their FailureInfo body names; 400 -> ArgumentException rebuilt from the
	// BadRequestResultInfo body. 401 is left as the transport's AuthenticationException: no facade
	// contract covers "you did not authenticate". A failure without a recognizable body is rethrown
	// as the transport raised it.
	internal static class ApiFailures
	{
		internal static readonly JsonSerializerOptions JsonOptions = new( JsonSerializerDefaults.Web );

		internal static async Task<T> TranslateAsync<T>( Func<Task<T>> call )
		{
			try
			{
				return await call().ConfigureAwait( false );
			}
			catch( AuthorizationException )
			{
				throw new AccessDeniedException();
			}
			catch( HttpApiRequestException exception ) when( Translate( exception ) is { } translated )
			{
				throw translated;
			}
		}

		private static Exception? Translate( HttpApiRequestException exception )
		{
			switch( exception.ResponseStatusCode )
			{
				case HttpStatusCode.BadRequest:
					return AsArgumentException( exception );

				case HttpStatusCode.NotFound:
				case HttpStatusCode.Conflict:
				case (HttpStatusCode)422:
					FailureInfo? failure = Deserialize<FailureInfo>( exception.ResponseMessage );

					return failure is null ? null : AsException( failure );

				default:
					return null;
			}
		}

		private static Exception? AsException( FailureInfo failure )
		{
			switch( failure.Type )
			{
				case FailureTypes.UnknownCountry:
					return new UnknownCountryException( failure.Country );

				case FailureTypes.UnknownSubdivision:
					return new UnknownSubdivisionException( failure.Country, failure.Subdivision );

				case FailureTypes.UnknownLocality:
					return new UnknownLocalityException( failure.Country, failure.Subdivision, failure.Postcode, failure.Name );

				case FailureTypes.UnknownCategory:
					return new UnknownCategoryException( failure.Country, failure.Category );

				case FailureTypes.DuplicateEntry:
					return new DuplicateEntryException( failure.EntityType, failure.Key );

				case FailureTypes.CountryConfiguration:
					return new CountryConfigurationException( failure.Country, failure.Message ?? string.Empty );

				case FailureTypes.UnknownImporter:
					return new UnknownDatasetImporterException( failure.Importer );

				case FailureTypes.InvalidDataset:
					return new DatasetImportException( WithoutPosition( failure.Message ?? string.Empty, failure.Position ), failure.Position );

				default:
					return null;
			}
		}

		// DatasetImportException appends " (at <position>)" to its message; the rebuilt one adds it
		// again, so it is removed from the server's message first.
		private static string WithoutPosition( string message, long? position )
		{
			string suffix = position is null ? string.Empty : $" (at {position})";

			return suffix.Length > 0 && message.EndsWith( suffix, StringComparison.Ordinal )
					? message.Substring( 0, message.Length - suffix.Length )
					: message;
		}

		// A body that does not deserialize still yields an ArgumentException, carrying whatever the
		// server said.
		private static ArgumentException AsArgumentException( HttpApiRequestException exception )
		{
			BadRequestResultInfo? failure = Deserialize<BadRequestResultInfo>( exception.ResponseMessage );

			if( failure is null || string.IsNullOrEmpty( failure.RequestArgument ) )
				return new ArgumentException( exception.ResponseMessage ?? exception.Message );

			return new ArgumentException( string.IsNullOrEmpty( failure.Message ) ? failure.Type : failure.Message, failure.RequestArgument );
		}

		private static T? Deserialize<T>( string? body ) where T : class
		{
			if( string.IsNullOrWhiteSpace( body ) )
				return null;

			try
			{
				return JsonSerializer.Deserialize<T>( body!, JsonOptions );
			}
			catch( JsonException )
			{
				return null;
			}
		}
	}
}
