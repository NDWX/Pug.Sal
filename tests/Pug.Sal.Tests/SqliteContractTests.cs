using Pug.Sal.Tests.Infrastructure;

namespace Pug.Sal.Tests;

public sealed class SqliteStoreTests : StoreContractTests<SalDatabaseFixture>
{
	public SqliteStoreTests( SalDatabaseFixture fixture ) : base( fixture )
	{
	}
}

public sealed class SqliteLocalityAdministrationTests : LocalityAdministrationContractTests<SalDatabaseFixture>
{
	public SqliteLocalityAdministrationTests( SalDatabaseFixture fixture ) : base( fixture )
	{
	}
}
