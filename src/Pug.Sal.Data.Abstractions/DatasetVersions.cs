using System.Globalization;

namespace Pug.Sal.Data.Abstractions
{
	// The country dataset version rule (DECISIONS D6), and its stored form, shared by every store.
	public static class DatasetVersions
	{
		// The stored form of every version-valued column: the UTC timestamp's digits to the tick.
		// Fixed width, so text order is time order in any store, and exact to the tick in any
		// store, whatever the precision of its own timestamp type.
		public const string TextFormat = "yyyyMMddHHmmssfffffff";

		// The smallest step between two versions.
		public static readonly TimeSpan Step = TimeSpan.FromTicks( 1 );

		// The version following previous: utcNow, or previous plus one Step when that is not
		// later (two writes in the same tick, or a clock that went backwards). Always UTC and
		// strictly greater than previous.
		public static DateTime Next( DateTime previous, DateTime utcNow )
		{
			DateTime following = previous + Step;

			return DateTime.SpecifyKind( utcNow > following ? utcNow : following, DateTimeKind.Utc );
		}

		// version (UTC) in its stored form.
		public static string ToText( DateTime version ) =>
			version.ToString( TextFormat, CultureInfo.InvariantCulture );

		// A stored version back as a UTC DateTime.
		public static DateTime FromText( string text ) =>
			DateTime.SpecifyKind(
					DateTime.ParseExact( text, TextFormat, CultureInfo.InvariantCulture ),
					DateTimeKind.Utc
				);
	}
}
