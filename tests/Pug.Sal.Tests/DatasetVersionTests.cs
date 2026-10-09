using Pug.Sal.Data.Abstractions;

namespace Pug.Sal.Tests;

public class DatasetVersionTests
{
	private static readonly DateTime Previous = new( 2026, 1, 1, 10, 0, 0, DateTimeKind.Utc );

	[Fact]
	public void LaterClockGivesClockToTheTick()
	{
		DateTime now = Previous.AddSeconds( 5 ).AddTicks( 37 );

		DateTime next = DatasetVersions.Next( Previous, now );

		Assert.Equal( now, next );
		Assert.Equal( DateTimeKind.Utc, next.Kind );
	}

	[Fact]
	public void SameTickGivesPreviousPlusOneTick()
	{
		DateTime next = DatasetVersions.Next( Previous, Previous );

		Assert.Equal( Previous.AddTicks( 1 ), next );
	}

	[Fact]
	public void ClockBehindGivesPreviousPlusOneTick()
	{
		DateTime next = DatasetVersions.Next( Previous, Previous.AddMinutes( -3 ) );

		Assert.Equal( Previous.AddTicks( 1 ), next );
		Assert.Equal( DateTimeKind.Utc, next.Kind );
	}

	[Fact]
	public void TextIsFixedWidthDigitsThatRoundTripAndSortInTimeOrder()
	{
		DateTime[] versions = { Previous, Previous.AddTicks( 1 ), Previous.AddTicks( 1_000_000 ), Previous.AddTicks( 1_200_000 ), Previous.AddSeconds( 1 ) };

		string[] texts = versions.Select( DatasetVersions.ToText ).ToArray();

		Assert.Equal( "202601011000000000000", texts[ 0 ] );
		Assert.All( texts, text => Assert.Matches( "^[0-9]{21}$", text ) );
		Assert.Equal( texts, texts.OrderBy( text => text, StringComparer.Ordinal ) );
		Assert.Equal( versions, texts.Select( DatasetVersions.FromText ) );
		Assert.All( texts.Select( DatasetVersions.FromText ), version => Assert.Equal( DateTimeKind.Utc, version.Kind ) );
	}
}
