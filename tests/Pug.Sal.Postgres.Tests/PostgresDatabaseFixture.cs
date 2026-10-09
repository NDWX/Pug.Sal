using Dapper;
using Npgsql;
using Pug.Application.Data;
using Pug.Sal.Data.Abstractions;
using Pug.Sal.Tests.Infrastructure;
using Testcontainers.PostgreSql;
using DapperData = Pug.Sal.Data.Postgres.DapperData;

namespace Pug.Sal.Tests;

// A PostgreSQL container per fixture, migrated with the store's own schema scripts (applied in
// order, as DbUp would).
public sealed class PostgresDatabaseFixture : IStoreFixture, IAsyncLifetime
{
	// The official image through AWS's public mirror of Docker Hub's library: anonymous Docker Hub
	// pulls are capped per IP over hours, which shared CI runners exhaust (the Ryuk image is
	// redirected the same way in postgres.runsettings).
	public const string Image = "public.ecr.aws/docker/library/postgres:17-alpine";

	private readonly PostgreSqlContainer _container = new PostgreSqlBuilder( Image ).Build();

	public IApplicationData<IDataSession> ApplicationData { get; private set; } = null!;

	public string ConnectionString { get; private set; } = null!;

	public async Task InitializeAsync()
	{
		await _container.StartAsync();

		ConnectionString = _container.GetConnectionString();

		DapperData data = new( ConnectionString, NpgsqlFactory.Instance );

		await using( NpgsqlConnection connection = new( ConnectionString ) )
		{
			await connection.OpenAsync();

			foreach( SchemaUpgradeScript script in data.GetSchemaUpgradeScripts() )
				await connection.ExecuteAsync( script.UpgradeScript.Script );
		}

		ApplicationData = data;
	}

	public async Task<string[]> QueryTextAsync( string sql, object parameters )
	{
		await using NpgsqlConnection connection = new( ConnectionString );

		return ( await connection.QueryAsync<string>( sql, parameters ) ).ToArray();
	}

	public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
