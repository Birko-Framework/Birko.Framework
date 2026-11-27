# Birko.Data.EventSourcing.RavenDB

An event store (`IAsyncEventStore`) on RavenDB: the RavenDB override of Birko.Data.EventSourcing's default event store.
A batch of events is saved in one RavenDB transaction, so it is stored whole or not at all.

## Features

- Append with per-aggregate versions. A second writer of one version, in any process, gets
  `EventStoreConcurrencyException`.
- An append of several events is atomic, even if the process dies partway.
- Reads an aggregate's history (in full, up to a version, or from a version), its current version, and all events from a
  point in time.
- Chosen by configuration: the application asks the store factory and never names RavenDB.

## Usage

```csharp
var factory = StoreFactory.Create(config["Store:Backend"], config["Store:ConnectionString"], p => p
    .AddSqLite().AddRavenDB());

IAsyncEventStore events = factory.GetEventStore(o => o.AddSql().AddRavenDB());
await events.AppendRangeAsync(new IEvent[] { created, renamed });
```

With `RavenDB` configured, the connection string is `Url=http://host:8080;Database=app`.

## Running tests

```sh
cd tests/Birko.Data.EventSourcing.RavenDB.Tests
BIRKO_RAVEN_URL=http://localhost:8080 dotnet test --nologo
```

Without `BIRKO_RAVEN_URL` the tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../LICENSE).
