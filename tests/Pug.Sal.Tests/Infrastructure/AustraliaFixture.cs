using Pug.Application.Data;
using Pug.Application.Security;
using Pug.Effable;
using Pug.Sal.Abstractions;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal.Tests.Infrastructure;

// A store seeded, through the store itself, with a small Australian dataset plus a country without
// subdivisions or postcodes (NR) and a retired country (YU). Read-only: tests must not write to it.
//
// AU (^\d{4}$, postcode required) subdivisions: AU-NSW, AU-VIC, AU-QLD, AU-ACT, and AU-JBT
// (retired). Localities:
// - AU-NSW: Sydney 2000; Sydney 2001 (retired); Environa 2620; Parkes 2870; Wagga Wagga 2650.
// - AU-ACT: Hume 2620 (2620 spans NSW and ACT); Parkes 2600 (a name in two subdivisions).
// - AU-VIC: Melbourne 3000; St Kilda 3182.
// - AU-QLD: Spring Creek 4343; Spring Creek 4361 (one name, two postcodes).
// - AU-JBT: Jervis Bay 2540 (current, under a retired subdivision).
// NR: Yaren (no subdivision, no postcode).
// Categories (DECISIONS D28): global SUBURB, TOWN, RURAL and HAMLET (retired). AU has SUBURB, TOWN
// and RURAL (disabled); NR has TOWN. Sydney, Melbourne, St Kilda and Hume are SUBURBs, Spring Creek
// 4361 is RURAL, every other locality is a TOWN.
public sealed class AustraliaFixture : IAsyncLifetime
{
	public static readonly DateTime AuVersion = new( 2026, 3, 1, 8, 30, 15, DateTimeKind.Utc );

	public static readonly DateTime NrVersion = AuVersion.AddDays( 1 );

	private static readonly DateTime Registered = AuVersion.AddDays( -10 );

	private readonly SalDatabaseFixture _database = new();

	public INameNormalizer Normalizer { get; } = new NameNormalizer();

	public IApplicationData<IDataSession> ApplicationData => _database.ApplicationData;

	public LocalityDirectory CreateDirectory( ISecurityManager? securityManager = null ) =>
		new( ApplicationData, securityManager ?? SecurityManagers.Authorized(), Normalizer );

	public async Task InitializeAsync()
	{
		await _database.InitializeAsync();

		await ApplicationData.PerformAsync(
			async ( session, context ) =>
			{
				await session.InsertCountryAsync( CountryRecord( "AU", "AUS", "Australia", @"^\d{4}$", true, true, AuVersion ) );
				await session.InsertCountryAsync( CountryRecord( "NR", "NRU", "Nauru", null, false, false, NrVersion ) );
				await session.InsertCountryAsync( CountryRecord( "YU", "YUG", "Yugoslavia", null, false, true, AuVersion ) with
				{
					Retired = Registered.AddDays( 1 )
				} );

				await session.InsertCategoryAsync( CategoryRecord( "SUBURB", "Suburb" ) );
				await session.InsertCategoryAsync( CategoryRecord( "TOWN", "Town" ) );
				await session.InsertCategoryAsync( CategoryRecord( "RURAL", "Rural locality" ) );
				await session.InsertCategoryAsync( CategoryRecord( "HAMLET", "Hamlet" ) with { Retired = Registered.AddDays( 1 ) } );

				await session.InsertCountryCategoryAsync( CountryCategoryRecord( "AU", "SUBURB" ) );
				await session.InsertCountryCategoryAsync( CountryCategoryRecord( "AU", "TOWN" ) );
				await session.InsertCountryCategoryAsync( CountryCategoryRecord( "AU", "RURAL" ) with { Disabled = Registered.AddDays( 4 ) } );
				await session.InsertCountryCategoryAsync( CountryCategoryRecord( "NR", "TOWN" ) );

				await session.InsertSubdivisionAsync( SubdivisionRecord( "AU-NSW", "New South Wales", SubdivisionTypes.State ) );
				await session.InsertSubdivisionAsync( SubdivisionRecord( "AU-VIC", "Victoria", SubdivisionTypes.State ) );
				await session.InsertSubdivisionAsync( SubdivisionRecord( "AU-QLD", "Queensland", SubdivisionTypes.State ) );
				await session.InsertSubdivisionAsync( SubdivisionRecord( "AU-ACT", "Australian Capital Territory", SubdivisionTypes.Territory ) );
				await session.InsertSubdivisionAsync( SubdivisionRecord( "AU-JBT", "Jervis Bay Territory", SubdivisionTypes.Territory ) with
				{
					Retired = Registered.AddDays( 2 )
				} );

				foreach( Locality locality in new[]
						{
							LocalityRecord( "AU", "AU-NSW", "2000", "Sydney", "SUBURB" ),
							LocalityRecord( "AU", "AU-NSW", "2001", "Sydney", "SUBURB" ) with { Retired = Registered.AddDays( 3 ) },
							LocalityRecord( "AU", "AU-NSW", "2620", "Environa" ),
							LocalityRecord( "AU", "AU-NSW", "2870", "Parkes" ),
							LocalityRecord( "AU", "AU-NSW", "2650", "Wagga Wagga" ),
							LocalityRecord( "AU", "AU-ACT", "2620", "Hume", "SUBURB" ),
							LocalityRecord( "AU", "AU-ACT", "2600", "Parkes" ),
							LocalityRecord( "AU", "AU-VIC", "3000", "Melbourne", "SUBURB" ),
							LocalityRecord( "AU", "AU-VIC", "3182", "St Kilda", "SUBURB" ),
							LocalityRecord( "AU", "AU-QLD", "4343", "Spring Creek" ),
							LocalityRecord( "AU", "AU-QLD", "4361", "Spring Creek", "RURAL" ),
							LocalityRecord( "AU", "AU-JBT", "2540", "Jervis Bay" ),
							LocalityRecord( "NR", null, null, "Yaren" )
						} )
				{
					await session.InsertLocalityAsync( locality,
						context.normalizer.NormalizePostcode( locality.Definition.Postcode ),
						context.normalizer.Normalize( locality.Definition.Name ) );
				}
			},
			context: new { normalizer = Normalizer } );
	}

	public Task DisposeAsync() => _database.DisposeAsync();

	private static ActionContext<Reference> Registration() =>
		new() { Actor = new Reference { Type = "USER", Identifier = "seed" }, Timestamp = Registered };

	private static Country CountryRecord( string code, string alpha3, string name, string? pattern, bool postcodeRequired,
										bool hasSubdivisions, DateTime version ) =>
		new()
		{
			Definition = new CountryDefinition
			{
				Code = code,
				Alpha3 = alpha3,
				Name = name,
				PostcodePattern = pattern!,
				PostcodeRequired = postcodeRequired,
				HasSubdivisions = hasSubdivisions
			},
			CurrentVersion = version,
			Registration = Registration()
		};

	private static Subdivision SubdivisionRecord( string code, string name, string type ) =>
		new()
		{
			Definition = new SubdivisionDefinition { Country = "AU", Code = code, Name = name, Type = type },
			Registration = Registration()
		};

	private static Locality LocalityRecord( string country, string? subdivision, string? postcode, string name, string category = "TOWN" ) =>
		new()
		{
			Definition = new LocalityDefinition
			{
				Country = country,
				Subdivision = subdivision!,
				Postcode = postcode!,
				Name = name,
				Category = category
			},
			Registration = Registration()
		};

	private static Category CategoryRecord( string code, string name ) =>
		new() { Definition = new CategoryDefinition { Code = code, Name = name }, Registration = Registration() };

	private static CountryCategory CountryCategoryRecord( string country, string category ) =>
		new() { Country = country, Category = category, Name = null!, Registration = Registration() };
}
