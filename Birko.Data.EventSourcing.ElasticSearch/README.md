# Birko.Data.EventSourcing.ElasticSearch

An event store (`IAsyncEventStore`) on Elasticsearch: the Elasticsearch override of Birko.Data.EventSourcing's default
event store. An appended event is visible to every reader as soon as the append returns.

## Features

- Append with per-aggregate versions. A second writer of one version, in any process, gets
  `EventStoreConcurrencyException`.
- Reads an aggregate's history (in full, up to a version, or from a version), its current version, and all events from a
  point in time.
- A batch of events shows all or nothing: a batch cut short by a crash is never read, and is cleared by a later append
  once it is older than `AbandonedBatchLease` (30 s).
- Chosen by configuration: the application asks the store factory and never names Elasticsearch.

## Usage

```csharp
var factory = StoreFactory.Create(config["Store:Backend"], config["Store:ConnectionString"], p => p
    .AddSqLite().AddPostgreSQL().AddMongoDB().AddElasticSearch());

IAsyncEventStore events = factory.GetEventStore(o => o
    .AddSql()               // Birko.Data.EventSourcing.SQL
    .AddElasticSearch());   // this project

var next = await events.GetVersionAsync(specificationId) + 1;
try
{
    await events.AppendAsync(new DomainEvent(specificationId, next, "Renamed", json));
}
catch (EventStoreConcurrencyException)
{
    // another writer appended this version first: reload and retry
}
```

With `ElasticSearch` configured, the connection string is `Url=http://host:9200;IndexPrefix=app`, and events go to the
index `app_eventrecord`.

## Running tests

```sh
cd tests/Birko.Data.EventSourcing.ElasticSearch.Tests
BIRKO_ES_URL=http://localhost:9200 dotnet test --nologo
```

Without `BIRKO_ES_URL` the tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../LICENSE).
