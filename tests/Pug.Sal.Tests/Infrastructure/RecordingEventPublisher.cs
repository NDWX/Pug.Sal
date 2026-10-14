using System.Collections.Concurrent;
using Pug.Sal.Abstractions;
using Pug.Sal.Models;

namespace Pug.Sal.Tests.Infrastructure;

// Records what LocalityAdministration publishes; optionally fails every call after recording it.
public sealed class RecordingEventPublisher : ILocalityChangeEventPublisher
{
	private readonly bool _fail;

	public RecordingEventPublisher( bool fail = false )
	{
		_fail = fail;
	}

	public ConcurrentQueue<LocalityChange> Changes { get; } = new();

	public ConcurrentQueue<ImportRun> Imports { get; } = new();

	public Task PublishAsync( LocalityChange change )
	{
		Changes.Enqueue( change );

		return _fail ? Task.FromException( new InvalidOperationException( "Broker down" ) ) : Task.CompletedTask;
	}

	public Task PublishImportAsync( ImportRun run )
	{
		Imports.Enqueue( run );

		return _fail ? Task.FromException( new InvalidOperationException( "Broker down" ) ) : Task.CompletedTask;
	}
}
