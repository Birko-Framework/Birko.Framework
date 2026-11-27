# Birko.Data.EventSourcing.RavenDB.Tests

Live tests for [Birko.Data.EventSourcing.RavenDB](../../Birko.Data.EventSourcing.RavenDB/README.md): the
shared event store contract on RavenDB.

## Test framework

xUnit + FluentAssertions.

## Running tests

```sh
BIRKO_RAVEN_URL=http://localhost:8080 dotnet test --nologo
```

Without `BIRKO_RAVEN_URL` the tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../../LICENSE).
