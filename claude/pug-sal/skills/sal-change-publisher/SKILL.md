---
name: sal-change-publisher
description: Use when writing a Pug.Sal ILocalityChangeEventPublisher, to send Sal's committed changes and import runs to a message broker, an audit log, a cache invalidator or webhooks (Azure Service Bus, RabbitMQ, Kafka, an outbox table), or when working out what Sal's change events contain and guarantee.
---

# Writing a Pug.Sal change event publisher

Sal publishes an event after every committed administration write. By default it uses a no-op publisher (`NullLocalityChangeEventPublisher`), and the host replaces it to forward events somewhere (DECISIONS D7).

```csharp
public interface ILocalityChangeEventPublisher
{
	Task PublishAsync( LocalityChange change );   // one admin write
	Task PublishImportAsync( ImportRun run );     // one completed import, instead of per-row changes
}
```

## What is published

- **`PublishAsync( LocalityChange )`** is called once for each create, update, retire, reinstate or delete made through `ILocalityAdministration`, on a country, subdivision or locality. The fields:
  - `Country`;
  - `EntityType` (`ChangeEntityTypes`);
  - `Key`, which is the country code, the subdivision code or the locality name, depending on `EntityType`;
  - for a locality, `Subdivision` and `Postcode`;
  - `Kind` (`ChangeKinds`);
  - `Performed`, an `ActionContext<Reference>` holding who and when;
  - `DatasetVersion`, the country version the write produced.
- **`PublishImportAsync( ImportRun )`** is called once per import, with the stored run as the payload: identifier, country, importer, source, `Added`, `Reinstated`, `Deleted`, `Unchanged`, `DatasetVersion` and `Performed`. **An import publishes no `LocalityChange` for its rows.** A consumer that needs row-level detail re-reads the country at the new `DatasetVersion`.
- **Reads publish nothing.**

`DatasetVersion` strictly increases per country. Consumers use it to order events and to discard stale ones, and a cache keyed by country version can be invalidated with it. Events for different countries are unordered relative to one another.

## Delivery guarantees, and what follows from them

Publishing is **after commit and best-effort**:

- The publisher is called only once the write has committed. It never sees a write that rolled back.
- **An exception from the publisher is logged as a warning and swallowed** (`LocalityAdministration.PublishAsync`). The write stands, and the caller gets success. There's no retry.
- **So events can be lost**, for example in a broker outage or a process crash between the commit and the publish. An exactly-once audit trail needs a transactional outbox, which D7 deferred. If you need one, your publisher can write to a durable local queue that's drained separately, but it still runs after the commit, so it narrows the gap without closing it. Say so in your component's docs. Don't claim stronger delivery than this.
- **The call is awaited on the caller's request path.** Publish latency adds to every admin write. Keep it short: hand the event to a client-side buffer or channel, or apply a timeout. Never block on a slow broker.

## Rules

- **Never throw to signal anything.** Sal ignores it. Log inside the publisher if you want better diagnostics than Sal's warning.
- **Don't call back into Sal** from inside the publisher, and don't write to the store.
- **It is a singleton.** It must be thread-safe, because concurrent writes publish concurrently. If it needs a scoped service (a `DbContext` for an outbox, for example), inject `IServiceScopeFactory` and create a scope for each call.
- **Serialize the records as they are.** `LocalityChange` and `ImportRun` are `[DataContract]` records in `Pug.Sal.Models`, so consumers can reference that package to deserialize them. Don't invent a parallel schema unless the broker's consumers can't take a .NET dependency.

## Implementing and registering

Reference only `Pug.Sal.Abstractions` (with `Pug.Sal.Models`).

```csharp
public sealed class ServiceBusChangePublisher : ILocalityChangeEventPublisher
{
	private readonly ServiceBusSender _sender;   // thread-safe, singleton

	public ServiceBusChangePublisher( ServiceBusSender sender ) => _sender = sender;

	public Task PublishAsync( LocalityChange change ) =>
		_sender.SendMessageAsync( Message( "locality-change", change.Country, change ) );

	public Task PublishImportAsync( ImportRun run ) =>
		_sender.SendMessageAsync( Message( "import-run", run.Country, run ) );

	// Body = JSON of the record, Subject = event type, SessionId/partition key = country (ordering).
	private static ServiceBusMessage Message<T>( string type, string country, T body ) => /* ... */;
}

services.AddSalChangePublisher<ServiceBusChangePublisher>();   // replaces the no-op, before or after AddSal
```

## Testing

Check that it formats both event types correctly, and that it behaves when the broker fails: no unbounded wait, and the failure is logged. Sal's own guarantee that a failing publisher never fails the write is already covered by `APublisherFailureNeverFailsTheWrite` in Sal's contract suite. `tests/Pug.Sal.Tests/Infrastructure/RecordingEventPublisher.cs` is a minimal publisher to copy into a host's tests.
