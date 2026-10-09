namespace Pug.Sal.Tests;

public sealed class PostgresStoreTests : StoreContractTests<PostgresDatabaseFixture>
{
	public PostgresStoreTests( PostgresDatabaseFixture fixture ) : base( fixture )
	{
	}
}

public sealed class PostgresLocalityAdministrationTests : LocalityAdministrationContractTests<PostgresDatabaseFixture>
{
	public PostgresLocalityAdministrationTests( PostgresDatabaseFixture fixture ) : base( fixture )
	{
	}
}
