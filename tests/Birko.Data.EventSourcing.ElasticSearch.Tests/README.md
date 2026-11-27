# Birko.Data.EventSourcing.ElasticSearch.Tests

Live tests for [Birko.Data.EventSourcing.ElasticSearch](../../Birko.Data.EventSourcing.ElasticSearch/README.md): the
shared event store contract on Elasticsearch.

## Test framework

xUnit + FluentAssertions.

## Running tests

```sh
BIRKO_ES_URL=http://localhost:9200 dotnet test --nologo
```

Without `BIRKO_ES_URL` the tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../../LICENSE).
