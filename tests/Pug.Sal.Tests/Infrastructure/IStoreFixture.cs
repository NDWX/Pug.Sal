using Pug.Application.Data;
using Pug.Sal.Data.Abstractions;

namespace Pug.Sal.Tests.Infrastructure;

// A migrated, empty Sal store the contract test suites run against, whichever store it is.
public interface IStoreFixture
{
	IApplicationData<IDataSession> ApplicationData { get; }

	// Runs a query directly against the store, outside Sal, for tests that inspect stored values.
	Task<string[]> QueryTextAsync( string sql, object parameters );
}
