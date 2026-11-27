# Birko.Data.EventSourcing.CosmosDB

An event store (`IAsyncEventStore`) on Azure Cosmos DB. Each aggregate's events are one logical partition: an append is
one transactional batch, and reading an aggregate's history is a single-partition query.

## Features

- Append with per-aggregate versions. A second writer of one version, in any process, gets
  `EventStoreConcurrencyException`.
- An append of several events (one aggregate, up to 100) is atomic.
- Reading an aggregate's history (in full, up to a version, or from a version) and its current version stays within
  its partition. Reading all events from a point in time spans partitions.
- Chosen by configuration: the application asks the store factory and never names Cosmos DB.

## Usage

```csharp
var factory = StoreFactory.Create(config["Store:Backend"], config["Store:ConnectionString"], p => p
    .AddSqLite().AddCosmosDB());

IAsyncEventStore events = factory.GetEventStore(o => o.AddSql().AddCosmosDB());
await events.AppendRangeAsync(new IEvent[] { created, renamed });   // one aggregate per batch
```

With `CosmosDB` configured, the connection string is `AccountEndpoint=…;AccountKey=…;Database=app`. Events go to the
container `EventStreams` in that database, partitioned on `/aggregateGuid`.

## Running tests

```sh
cd tests/Birko.Data.EventSourcing.CosmosDB.Tests
BIRKO_COSMOS_CONNECTION="AccountEndpoint=http://localhost:8081/;AccountKey=…" BIRKO_COSMOS_CONNECTION_MODE=Gateway dotnet test --nologo
```

Without `BIRKO_COSMOS_CONNECTION` the tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../LICENSE).
