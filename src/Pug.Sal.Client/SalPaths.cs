namespace Pug.Sal.Client
{
	// Paths and queries of the sal/ endpoints, as Pug.Sal.WebApi maps them.
	internal static class SalPaths
	{
		public static string Escape( string value ) => Uri.EscapeDataString( value ?? string.Empty );

		public static string Country( string root, string country ) => $"{root}/countries/{Escape( country )}";

		// Pug.HttpApiClient 0.6.0 aggregates the query collection without a seed, so an empty (but
		// non-null) dictionary throws where null is handled.
		public static IDictionary<string, string>? Queries( params (string Name, string? Value)[] queries )
		{
			Dictionary<string, string> given = new();

			foreach( ( string name, string? value ) in queries )
			{
				if( value is not null )
					given[ name ] = value;
			}

			return given.Count > 0 ? given : null;
		}

		public static string? Flag( bool value ) => value ? "true" : null;

		public static IDictionary<string, string>? LocalityKey( string? subdivision, string? postcode, string? name ) =>
			Queries( ( "subdivision", subdivision ), ( "postcode", postcode ), ( "name", name ) );
	}
}
