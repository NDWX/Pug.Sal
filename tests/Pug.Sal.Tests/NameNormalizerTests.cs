namespace Pug.Sal.Tests;

public class NameNormalizerTests
{
	private readonly NameNormalizer _normalizer = new();

	[Theory]
	[InlineData( "St Kilda", "ST KILDA" )]
	[InlineData( "  st   kilda  ", "ST KILDA" )]
	[InlineData( "st\tkilda\n", "ST KILDA" )]
	[InlineData( "Mönchengladbach", "MONCHENGLADBACH" )]
	[InlineData( "Bègles", "BEGLES" )]
	[InlineData( "Wagga Wagga", "WAGGA WAGGA" )]
	[InlineData( "O'Connor", "O'CONNOR" )]
	[InlineData( "   ", "" )]
	public void NormalizesNames( string name, string expected )
	{
		Assert.Equal( expected, _normalizer.Normalize( name ) );
	}

	[Fact]
	public void NullNameStaysNull()
	{
		Assert.Null( _normalizer.Normalize( null ) );
	}

	[Theory]
	[InlineData( "2000", "2000" )]
	[InlineData( " 2000 ", "2000" )]
	[InlineData( "sw1a  1aa", "SW1A 1AA" )]
	[InlineData( null, null )]
	[InlineData( "", null )]
	[InlineData( "  ", null )]
	public void NormalizesPostcodes( string? postcode, string? expected )
	{
		Assert.Equal( expected, _normalizer.NormalizePostcode( postcode ) );
	}
}
