using System.Transactions;
using Pug.Application.Data;
using Pug.Application.Data.Extensions;

namespace Pug.Sal.Data.Abstractions
{
	// How Sal runs units of work against its store (DECISIONS D19). Each runs in a transaction of its
	// own (RequiresNew), never in a caller's ambient one, whose isolation level could conflict with
	// the one Sal needs:
	// - Writes run at ReadCommitted. Writers to a country serialize on the lock AdvanceVersionAsync
	//   takes; at a stricter level, Postgres would fail a writer that waited for that lock
	//   (could not serialize access due to concurrent update) instead of letting it proceed.
	// - Reads run at RepeatableRead, so every statement of one read sees the same snapshot; a
	//   read-only transaction never fails at that level.
	// SQLite (with the session's own enlistment, D15) is serializable whatever the level.
	public static class UnitsOfWork
	{
		private static readonly TransactionOptions ReadOptions = new() { IsolationLevel = IsolationLevel.RepeatableRead };

		private static readonly TransactionOptions WriteOptions = new() { IsolationLevel = IsolationLevel.ReadCommitted };

		public static Task<TResult> ReadAsync<TContext, TResult>( this IApplicationData<IDataSession> data,
																	Func<IDataSession, TContext, Task<TResult>> read,
																	TContext context ) =>
			data.ExecuteAsync( read, context, TransactionScopeOption.RequiresNew, ReadOptions );

		public static Task<TResult> WriteAsync<TContext, TResult>( this IApplicationData<IDataSession> data,
																	Func<IDataSession, TContext, Task<TResult>> write,
																	TContext context ) =>
			data.ExecuteAsync( write, context, TransactionScopeOption.RequiresNew, WriteOptions );
	}
}
