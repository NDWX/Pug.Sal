using Dapper;
using DbUp;
using DbUp.Builder;
using DbUp.Engine;
using Microsoft.Data.Sqlite;
using Pug.Application.Data;
using Pug.Sal.Data.Abstractions;
using DapperData = Pug.Sal.Data.SQLite.DapperData;

namespace Pug.Sal.Tests.Infrastructure;

/// <summary>
/// Provisions a fresh, isolated SQLite database for the Sal store (migrated from the
/// <see cref="DapperData"/> schema scripts).
/// </summary>
public sealed class SalDatabaseFixture : IStoreFixture, IAsyncLifetime
{
	private string? _dbPath;
	private bool _disposed;

	public IApplicationData<IDataSession> ApplicationData { get; private set; } = null!;

	// For tests that inspect stored values directly.
	public string ConnectionString { get; private set; } = null!;

	public Task InitializeAsync()
	{
		_dbPath = Path.Combine( Path.GetTempPath(), $"pug_sal_test_{Guid.NewGuid():N}.db" );

		// Pooling=False ensures each connection is physically closed when disposed.
		string connectionString = $"Data Source={_dbPath};Pooling=False;";
		ConnectionString = connectionString;

		DapperData data = new( connectionString, SqliteFactory.Instance );

		UpgradeDatabase( connectionString, data );

		ApplicationData = data;

		return Task.CompletedTask;
	}

	public async Task<string[]> QueryTextAsync( string sql, object parameters )
	{
		await using SqliteConnection connection = new( ConnectionString );

		return ( await connection.QueryAsync<string>( sql, parameters ) ).ToArray();
	}

	public Task DisposeAsync()
	{
		Cleanup();
		return Task.CompletedTask;
	}

	private static void UpgradeDatabase( string connectionString, DapperData data )
	{
		UpgradeEngineBuilder builder = DeployChanges.To.SQLiteDatabase( connectionString );

		foreach( SchemaUpgradeScript schemaScript in data.GetSchemaUpgradeScripts() )
			builder = builder.WithScript( schemaScript.UpgradeScript.Name, schemaScript.UpgradeScript.Script );

		DatabaseUpgradeResult result = builder.LogToNowhere().Build().PerformUpgrade();

		if( !result.Successful )
			throw new InvalidOperationException( "DbUp schema migration failed.", result.Error );

		using SqliteConnection connection = new( connectionString );
		connection.Open();
		using SqliteCommand cmd = connection.CreateCommand();
		cmd.CommandText = "PRAGMA journal_mode=WAL;";
		cmd.ExecuteNonQuery();
		cmd.CommandText = "PRAGMA busy_timeout=5000;";
		cmd.ExecuteNonQuery();
	}

	private void Cleanup()
	{
		if( _disposed ) return;
		_disposed = true;

		if( _dbPath != null && File.Exists( _dbPath ) )
			File.Delete( _dbPath );
	}
}
