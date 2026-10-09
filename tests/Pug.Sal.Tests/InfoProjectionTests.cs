using Pug.Effable;
using Pug.Sal.Models;

namespace Pug.Sal.Tests;

// ToInfo keeps everything but the audit contexts; that those are dropped is guaranteed by the *Info
// records having no such members.
public class InfoProjectionTests
{
	private static readonly DateTime Base = new( 2026, 3, 1, 8, 30, 15, DateTimeKind.Utc );

	private static ActionContext<Reference> Context( DateTime timestamp ) =>
		new() { Actor = new Reference { Type = "USER", Identifier = "alice" }, Timestamp = timestamp };

	[Fact]
	public void CountryToInfoKeepsDefinitionVersionAndRetired()
	{
		Country country = new()
		{
			Definition = new CountryDefinition { Code = "AU", Name = "Australia", PostcodeRequired = true, HasSubdivisions = true },
			CurrentVersion = Base.AddTicks( 7 ),
			Retired = Base.AddDays( 1 ),
			Registration = Context( Base ),
			LastUpdate = Context( Base.AddDays( 1 ) )
		};

		CountryInfo info = country.ToInfo();

		Assert.Equal( new CountryInfo { Definition = country.Definition, CurrentVersion = country.CurrentVersion, Retired = country.Retired }, info );
	}

	[Fact]
	public void SubdivisionToInfoKeepsDefinitionAndRetired()
	{
		Subdivision subdivision = new()
		{
			Definition = new SubdivisionDefinition { Country = "AU", Code = "AU-NSW", Name = "New South Wales", Type = SubdivisionTypes.State },
			Retired = Base,
			Registration = Context( Base )
		};

		Assert.Equal( new SubdivisionInfo { Definition = subdivision.Definition, Retired = Base }, subdivision.ToInfo() );
	}

	[Fact]
	public void LocalityToInfoKeepsDefinitionAndRetired()
	{
		Locality locality = new()
		{
			Definition = new LocalityDefinition { Country = "AU", Subdivision = "AU-NSW", Postcode = "2000", Name = "Sydney" },
			Registration = Context( Base ),
			LastUpdate = Context( Base.AddHours( 1 ) )
		};

		LocalityInfo info = locality.ToInfo();

		Assert.Equal( new LocalityInfo { Definition = locality.Definition, Retired = null }, info );
	}
}
