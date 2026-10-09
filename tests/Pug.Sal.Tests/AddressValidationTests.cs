using Pug.Sal.Models;
using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

// ILocalityDirectory.ValidateAsync against the Australian fixture (DECISIONS D3).
public class AddressValidationTests : IClassFixture<AustraliaFixture>
{
	private readonly LocalityDirectory _directory;

	public AddressValidationTests( AustraliaFixture fixture )
	{
		_directory = fixture.CreateDirectory();
	}

	private Task<ValidationResult> ValidateAsync( string country, string? subdivision, string? locality, string? postcode ) =>
		_directory.ValidateAsync( new AddressCandidate
		{
			Country = country,
			Subdivision = subdivision!,
			Locality = locality!,
			Postcode = postcode!
		} );

	private static string[] Suggested( ValidationResult result ) =>
		result.Suggestions.Select( s => $"{s.Subdivision}|{s.Postcode}|{s.Locality}" ).ToArray();

	private static void AssertOutcomes( ValidationResult result, string country, string subdivision, string locality,
										string postcode, string combination )
	{
		Assert.Equal(
			( country, subdivision, locality, postcode, combination ),
			( result.Country.Outcome, result.Subdivision.Outcome, result.Locality.Outcome, result.Postcode.Outcome,
				result.Combination ) );
	}

	[Theory]
	[InlineData( "AU-NSW" )]
	[InlineData( "nsw" )]
	[InlineData( "new south  wales" )]
	public async Task ValidAddressReportsCanonicalForms( string subdivision )
	{
		ValidationResult result = await ValidateAsync( "au", subdivision, "  sydney ", "2000" );

		Assert.True( result.IsValid );
		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Valid, ValidationOutcomes.Valid,
						ValidationOutcomes.Valid, ValidationOutcomes.Valid );
		Assert.Equal( "AU", result.Country.Canonical );
		Assert.Equal( "AU-NSW", result.Subdivision.Canonical );
		Assert.Equal( "Sydney", result.Locality.Canonical );
		Assert.Equal( "2000", result.Postcode.Canonical );
		Assert.Equal( new AddressCandidate { Country = "AU", Subdivision = "AU-NSW", Locality = "Sydney", Postcode = "2000" }, result.Match );
		Assert.Empty( result.Suggestions );
		Assert.Equal( AustraliaFixture.AuVersion, result.DatasetVersion );
	}

	[Fact]
	public async Task PostcodeSpanningSubdivisionsIsValidInEach()
	{
		Assert.True( ( await ValidateAsync( "AU", "NSW", "Environa", "2620" ) ).IsValid );
		Assert.True( ( await ValidateAsync( "AU", "ACT", "Hume", "2620" ) ).IsValid );
	}

	[Fact]
	public async Task WrongSubdivisionIsAMismatchSuggestingTheRightOne()
	{
		ValidationResult result = await ValidateAsync( "AU", "ACT", "Environa", "2620" );

		Assert.False( result.IsValid );
		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Valid, ValidationOutcomes.Valid,
						ValidationOutcomes.Valid, ValidationOutcomes.Mismatch );
		Assert.Null( result.Match );
		Assert.Equal( new[] { "AU-NSW|2620|Environa" }, Suggested( result ) );
	}

	[Fact]
	public async Task WrongPostcodeSuggestsTheLocalitysPostcodes()
	{
		ValidationResult result = await ValidateAsync( "AU", "QLD", "Spring Creek", "3000" );

		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Valid, ValidationOutcomes.Valid,
						ValidationOutcomes.Valid, ValidationOutcomes.Mismatch );
		Assert.Equal( new[] { "AU-QLD|4343|Spring Creek", "AU-QLD|4361|Spring Creek" }, Suggested( result ) );
	}

	[Fact]
	public async Task NameInAnotherSubdivisionSuggestsBothMatches()
	{
		ValidationResult result = await ValidateAsync( "AU", "NSW", "Parkes", "2600" );

		Assert.Equal( ValidationOutcomes.Mismatch, result.Combination );
		Assert.Equal( new[] { "AU-ACT|2600|Parkes", "AU-NSW|2870|Parkes" }, Suggested( result ) );
	}

	[Fact]
	public async Task MalformedPostcodeIsNotChecked()
	{
		ValidationResult result = await ValidateAsync( "AU", "NSW", "Sydney", "200A" );

		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Valid, ValidationOutcomes.Valid,
						ValidationOutcomes.Malformed, ValidationOutcomes.NotChecked );
		Assert.False( result.IsValid );
		Assert.Equal( new[] { "AU-NSW|2000|Sydney" }, Suggested( result ) );
	}

	[Fact]
	public async Task MissingComponents()
	{
		ValidationResult noPostcode = await ValidateAsync( "AU", "NSW", "Sydney", " " );
		ValidationResult nothing = await ValidateAsync( "AU", null, null, null );

		AssertOutcomes( noPostcode, ValidationOutcomes.Valid, ValidationOutcomes.Valid, ValidationOutcomes.Valid,
						ValidationOutcomes.Missing, ValidationOutcomes.NotChecked );
		Assert.Equal( new[] { "AU-NSW|2000|Sydney" }, Suggested( noPostcode ) );
		AssertOutcomes( nothing, ValidationOutcomes.Valid, ValidationOutcomes.Missing, ValidationOutcomes.Missing,
						ValidationOutcomes.Missing, ValidationOutcomes.NotChecked );
		Assert.Empty( nothing.Suggestions );
	}

	[Fact]
	public async Task UnknownLocalitySuggestsThePostcodesLocalities()
	{
		ValidationResult result = await ValidateAsync( "AU", "ACT", "Nowhere", "2620" );

		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Valid, ValidationOutcomes.Unknown,
						ValidationOutcomes.Valid, ValidationOutcomes.NotChecked );
		Assert.Equal( new[] { "AU-ACT|2620|Hume" }, Suggested( result ) );
	}

	[Fact]
	public async Task UnknownSubdivisionAndPostcode()
	{
		ValidationResult result = await ValidateAsync( "AU", "Tasmania", "Sydney", "7000" );

		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Unknown, ValidationOutcomes.Valid,
						ValidationOutcomes.Unknown, ValidationOutcomes.NotChecked );
		Assert.Equal( new[] { "AU-NSW|2000|Sydney" }, Suggested( result ) );
	}

	[Fact]
	public async Task RetiredLocalityIsReported()
	{
		ValidationResult result = await ValidateAsync( "AU", "NSW", "Sydney", "2001" );

		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Valid, ValidationOutcomes.Valid,
						ValidationOutcomes.Retired, ValidationOutcomes.Retired );
		Assert.False( result.IsValid );
		Assert.Equal( "2001", result.Postcode.Canonical );
		Assert.Equal( new[] { "AU-NSW|2000|Sydney" }, Suggested( result ) );
	}

	[Fact]
	public async Task RetiredSubdivisionIsReportedAndNotSuggested()
	{
		ValidationResult result = await ValidateAsync( "AU", "JBT", "Jervis Bay", "2540" );

		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.Retired, ValidationOutcomes.Valid,
						ValidationOutcomes.Valid, ValidationOutcomes.Valid );
		Assert.False( result.IsValid );
		Assert.Empty( result.Suggestions );
	}

	[Fact]
	public async Task CountryWithoutSubdivisionsOrPostcodes()
	{
		ValidationResult result = await ValidateAsync( "NR", "anything", "yaren", null );

		Assert.True( result.IsValid );
		AssertOutcomes( result, ValidationOutcomes.Valid, ValidationOutcomes.NotApplicable, ValidationOutcomes.Valid,
						ValidationOutcomes.NotApplicable, ValidationOutcomes.Valid );
		Assert.Equal( new AddressCandidate { Country = "NR", Locality = "Yaren" }, result.Match );
		Assert.Equal( AustraliaFixture.NrVersion, result.DatasetVersion );
	}

	[Fact]
	public async Task RetiredCountryIsReported()
	{
		ValidationResult result = await ValidateAsync( "YU", null, "Beograd", null );

		Assert.Equal( ValidationOutcomes.Retired, result.Country.Outcome );
		Assert.False( result.IsValid );
	}
}
