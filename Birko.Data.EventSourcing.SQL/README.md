# Birko.Data.EventSourcing.SQL

A SQL `IAsyncEventStore` for Birko event sourcing, on SQLite, PostgreSQL, SQL Server or MySQL.

```csharp
// Server SQL
var events = new SqlEventStore<PostgreSQLConnector>(postgresSettings);

// SQLite
var inner = new AsyncSQLiteStore<DomainEventModel>();
inner.SetSettings(new SqLiteSettings(dir, "events.db"));
var events = new SqlEventStore<SqLiteConnector>(inner);

var version = await events.GetVersionAsync(orderId);
try
{
    await events.AppendAsync(new DomainEvent(orderId, version + 1, "Shipped", json));
}
catch (EventStoreConcurrencyException)
{
    // another writer appended first — reload the aggregate and retry
}
```

- Events live in `__Events`; `(AggregateGuid, Version)` is unique in the database, so concurrent writers to one
  aggregate cannot both succeed.
- `AppendRangeAsync` is atomic and joins a surrounding `SqlUnitOfWork` on the same database.
- Plug it into the store wrappers: `store.WithEventSourcing(events)`.
