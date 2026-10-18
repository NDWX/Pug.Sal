using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Pug.Application.Data;
using Pug.Application.Security;
using Pug.HttpApiClient;
using Pug.Sal.Abstractions;
using Pug.Sal.Client;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.DependencyInjection;
using Pug.Sal.Importers.Csv;
using Pug.Sal.Models;
using Pug.Sal.WebApi;
using Pug.Sal.WebApi.Models;

namespace Pug.Sal.Tests.Infrastructure;

// Hosts MapSalEndpoints() over a TestServer against a real SQLite store, composed with the
// production DI extensions; only authentication and the security principal are faked. Users:
// "reader" holds VIEW, "admin" every operation, anyone else nothing.
//
// Seeded through the clients themselves: global categories SUBURB and TOWN; AU (^\d{4}$, postcode
// required) with both, and NSW, ACT and VIC; and a CSV import of Sydney 2000 (SUBURB), Environa 2620
// (NSW, TOWN), Hume 2620 (ACT, SUBURB by the defaultCategory option), St Kilda 3182 (SUBURB).
public class SalApiFixture : IAsyncLifetime
{
	public const string Reader = "reader";

	public const string Admin = "admin";

	private readonly SalDatabaseFixture _database = new();

	private IHost _host = null!;

	public IServiceProvider Services => _host.Services;

	public async Task InitializeAsync()
	{
		await _database.InitializeAsync();

		_host = await new HostBuilder()
					.ConfigureWebHost( web => web.UseTestServer().ConfigureServices( ConfigureServices ).Configure( Configure ) )
					.StartAsync();

		await SeedAsync();
	}

	protected virtual async Task SeedAsync()
	{
		LocalityAdministrationClient admin = AdministrationClient( Admin );

		await admin.CreateCategoryAsync( new CategoryDefinition { Code = "SUBURB", Name = "Suburb" } );
		await admin.CreateCategoryAsync( new CategoryDefinition { Code = "TOWN", Name = "Town" } );

		await admin.CreateCountryAsync( new CountryDefinition
		{
			Code = "AU", Alpha3 = "AUS", Name = "Australia", PostcodePattern = @"^\d{4}$", PostcodeRequired = true, HasSubdivisions = true,
			Categories = new[] { "SUBURB", "TOWN" }
		} );

		foreach( ( string code, string name ) in new[] { ( "NSW", "New South Wales" ), ( "ACT", "Australian Capital Territory" ), ( "VIC", "Victoria" ) } )
			await admin.CreateSubdivisionAsync( new SubdivisionDefinition { Country = "AU", Code = code, Name = name } );

		await admin.ImportAsync( "AU",
			new ImportRequest { Importer = "csv", Source = "seed", Options = new Dictionary<string, string> { [ "defaultCategory" ] = "SUBURB" } },
			new MemoryStream( Encoding.UTF8.GetBytes(
				"subdivision,postcode,locality,category\nNSW,2000,Sydney,SUBURB\nNSW,2620,Environa,TOWN\nACT,2620,Hume,\nVIC,3182,St Kilda,SUBURB\n" ) ) );
	}

	public async Task DisposeAsync()
	{
		await _host.StopAsync();
		_host.Dispose();
		await _database.DisposeAsync();
	}

	protected virtual void ConfigureServices( IServiceCollection services )
	{
		services.AddRouting();
		services.AddHttpContextAccessor();
		services.AddAuthentication( TestAuthenticationHandler.SchemeName )
				.AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>( TestAuthenticationHandler.SchemeName, _ => { } );
		services.AddAuthorization();

		services.AddSingleton<IApplicationData<IDataSession>>( _database.ApplicationData );
		services.AddSingleton<ISecurityManager>( provider => CurrentRequestSecurity( provider.GetRequiredService<IHttpContextAccessor>() ) );

		services.AddSalChangePublisher<RecordingEventPublisher>();
		services.AddSal();
		services.AddSalDatasetImporter<CsvDatasetImporter>( CsvDatasetImporter.DefaultName );
		services.AddSalDatasetImporter<OsAccessDeniedImporter>( OsAccessDeniedImporter.Name );
	}

	// Fails as reading a dataset file the server may not open would: with the operating system's
	// UnauthorizedAccessException, a server fault that must not reach the caller as a refusal.
	private sealed class OsAccessDeniedImporter : IDatasetImporter
	{
		public const string Name = "os-denied";

		public IAsyncEnumerable<ImportRow> ReadAsync( string country, Stream source, IDictionary<string, string> options ) =>
			throw new UnauthorizedAccessException( "Access to the path '/var/sal/dataset.csv' is denied." );
	}

	private void Configure( IApplicationBuilder app )
	{
		app.UseRouting();
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseEndpoints( MapEndpoints );
	}

	protected virtual void MapEndpoints( IEndpointRouteBuilder endpoints ) => endpoints.MapSalEndpoints();

	// The principal of the current request, granted by its user name.
	private static ISecurityManager CurrentRequestSecurity( IHttpContextAccessor accessor )
	{
		string User() => accessor.HttpContext?.User.Identity?.Name ?? string.Empty;

		Mock<IPrincipalIdentity> identity = new();
		identity.Setup( x => x.Identifier ).Returns( User );
		identity.Setup( x => x.IsAuthenticated ).Returns( () => accessor.HttpContext?.User.Identity?.IsAuthenticated == true );

		Mock<IPrincipal> principal = new();
		principal.Setup( x => x.Identity ).Returns( identity.Object );
		principal
			.Setup( x => x.IsAuthorizedAsync( It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<NounQualifier>(), It.IsAny<string>() ) )
			.ReturnsAsync( ( IDictionary<string, string> _, string operation, NounQualifier _, string _ ) =>
								User() == Admin || ( User() == Reader && operation == SecurityOperations.View ) );

		Mock<ISecurityManager> manager = new();
		manager.Setup( x => x.CurrentPrincipal ).Returns( principal.Object );

		return manager.Object;
	}

	// A raw client authenticated as user, holding claims (<type>=<value>); null for an
	// unauthenticated caller.
	public HttpClient CreateClient( string? user, params string[] claims )
	{
		HttpClient client = _host.GetTestClient();

		if( user is not null )
			client.DefaultRequestHeaders.Add( TestAuthenticationHandler.UserHeader, user );

		foreach( string claim in claims )
			client.DefaultRequestHeaders.Add( TestAuthenticationHandler.ClaimHeader, claim );

		return client;
	}

	public IHttpApiClient CreateApiClient( string? user ) =>
		new HttpApiClient.HttpApiClient( new Uri( "http://localhost/" ), new ClientFactory( this, user ) );

	// Where MapEndpoints mounts each surface, for the clients.
	protected virtual string DirectoryPrefix => SalRoutes.DefaultDirectoryPrefix;

	protected virtual string AdministrationPrefix => SalRoutes.DefaultAdministrationPrefix;

	public LocalityDirectoryClient DirectoryClient( string? user ) => new( CreateApiClient( user ), DirectoryPrefix );

	public LocalityAdministrationClient AdministrationClient( string? user ) => new( CreateApiClient( user ), AdministrationPrefix );

	public RecordingEventPublisher EventPublisher => (RecordingEventPublisher)Services.GetRequiredService<ILocalityChangeEventPublisher>();

	private sealed class ClientFactory : IHttpClientFactory
	{
		private readonly SalApiFixture _fixture;

		private readonly string? _user;

		public ClientFactory( SalApiFixture fixture, string? user )
		{
			_fixture = fixture;
			_user = user;
		}

		public HttpClient CreateClient( string name ) => _fixture.CreateClient( _user );
	}
}
