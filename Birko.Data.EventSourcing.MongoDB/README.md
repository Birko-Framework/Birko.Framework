# Birko.Data.EventSourcing.MongoDB

An event store (`IAsyncEventStore`) on MongoDB: the MongoDB override of Birko.Data.EventSourcing's default event store.
On a replica set or a sharded cluster, a batch of events is saved in one MongoDB transaction, so it is stored whole or
not at all.

## Features

- Append with per-aggregate versions. A second writer of one version, in any process, gets
  `EventStoreConcurrencyException`.
- An append of several events is atomic on a replica set or sharded cluster, and joins your own `MongoDbUnitOfWork` when
  one is active, so an entity change and its events commit together.
- On a standalone server (no transactions) a failed batch removes the events it already wrote; a crash partway can
  leave them.
- Chosen by configuration: the application asks the store factory and never names MongoDB.

## Usage

```csharp
var factory = StoreFactory.Create(config["Store:Backend"], config["Store:ConnectionString"], p => p
    .AddSqLite().AddMongoDB());

IAsyncEventStore events = factory.GetEventStore(o => o.AddSql().AddMongoDB());
await events.AppendRangeAsync(new IEvent[] { created, renamed });
```

With `MongoDB` configured, the connection string is MongoDB's own: `mongodb://host:27017/app`.

## Running tests

```sh
cd tests/Birko.Data.EventSourcing.MongoDB.Tests
BIRKO_MONGO_HOST=localhost BIRKO_MONGO_STANDALONE_URL=mongodb://localhost:27018/ dotnet test --nologo
```

`BIRKO_MONGO_HOST` must be a replica set (as in CI). `BIRKO_MONGO_STANDALONE_URL` is an optional standalone server.
Without a variable, its tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../LICENSE).
