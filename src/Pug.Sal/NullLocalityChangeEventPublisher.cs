using Pug.Sal.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal
{
	// The publisher used when the host registers none: change events go nowhere (DECISIONS D7).
	public sealed class NullLocalityChangeEventPublisher : ILocalityChangeEventPublisher
	{
		public static NullLocalityChangeEventPublisher Instance { get; } = new();

		public Task PublishAsync( LocalityChange change ) => Task.CompletedTask;

		public Task PublishImportAsync( ImportRun run ) => Task.CompletedTask;

		public Task PublishCategoryReplacementAsync( CategoryReplacement replacement ) => Task.CompletedTask;
	}
}
