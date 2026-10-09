using Pug.Effable;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal.Tests;

// What only the Postgres store does: close, non-prefix matches by pg_trgm similarity, after the
// prefix matches (DECISIONS D3).
public sealed class PostgresSearchTests : IClassFixture<PostgresDatabaseFixture>
{
	private readonly PostgresDatabaseFixture _fixture;

	public PostgresSearchTests( PostgresDatabaseFixture fixture )
	{
		_fixture = fixture;
	}

	private static Locality LocalityRecord( string name, string postcode ) =>
		new()
		{
			Definition = new LocalityDefinition { Country = "AU", Subdivision = "NSW", Postcode = postcode, Name = name },
			Registration = new ActionContext<Reference>
			{
				Actor = new Reference { Type = "USER", Identifier = "seed" },
				Timestamp = new DateTime( 2026, 3, 1, 0, 0, 0, DateTimeKind.Utc )
			}
		};

	[Fact]
	public async Task SimilarNamesFollowThePrefixMatches()
	{
		await _fixture.ApplicationData.WriteAsync(
			async ( session, _ ) =>
			{
				await session.InsertCountryAsync( new Country
				{
					Definition = new CountryDefinition { Code = "AU", Name = "Australia", PostcodeRequired = true, HasSubdivisions = true },
					CurrentVersion = new DateTime( 2026, 3, 1, 0, 0, 0, DateTimeKind.Utc ),
					Registration = LocalityRecord( "x", "0" ).Registration
				} );
				await session.InsertSubdivisionAsync( new Subdivision
				{
					Definition = new SubdivisionDefinition { Country = "AU", Code = "NSW", Name = "New South Wales" },
					Registration = LocalityRecord( "x", "0" ).Registration
				} );

				foreach( ( string name, string postcode ) in new[] { ( "Wagga Wagga", "2650" ), ( "Wagait Beach", "0822" ), ( "Sydney", "2000" ) } )
					await session.InsertLocalityAsync( LocalityRecord( name, postcode ), postcode, name.ToUpperInvariant() );

				return true;
			},
			context: new { } );

		Locality[] results = ( await _fixture.ApplicationData.ReadAsync(
									( session, _ ) => session.SearchLocalitiesAsync( "AU", "WAGA WAGGA", null, 10 ),
									context: new { } ) ).ToArray();

		Assert.Equal( "Wagga Wagga", Assert.Single( results ).Definition.Name );

		Locality[] prefixed = ( await _fixture.ApplicationData.ReadAsync(
									( session, _ ) => session.SearchLocalitiesAsync( "AU", "WAGA", null, 10 ),
									context: new { } ) ).ToArray();

		Assert.Equal( new[] { "Wagait Beach", "Wagga Wagga" }, prefixed.Select( l => l.Definition.Name ) );
	}
}
