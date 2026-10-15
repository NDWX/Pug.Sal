using System;

namespace Pug.Sal.WebApi.Models
{
	// Where each Sal surface is mounted (DECISIONS D20). The host chooses the prefixes when it maps
	// the endpoints, and the clients take the same ones; both default to these.
	public static class SalRoutes
	{
		public const string DefaultDirectoryPrefix = "directory";

		public const string DefaultAdministrationPrefix = "administration";

		// A prefix without its leading or trailing slashes; empty mounts the surface where it is mapped.
		public static string NormalizePrefix( string prefix )
		{
			if( prefix == null )
				throw new ArgumentNullException( nameof(prefix) );

			string normalized = prefix.Trim().Trim( '/' );

			if( normalized.Contains( "//" ) || normalized.Length != normalized.Trim().Length )
				throw new ArgumentException( "A route prefix must not contain empty segments or surrounding whitespace.", nameof(prefix) );

			return normalized;
		}
	}
}
