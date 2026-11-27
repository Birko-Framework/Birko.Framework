# Birko.Data.EventSourcing.MongoDB.Tests

Live tests for [Birko.Data.EventSourcing.MongoDB](../../Birko.Data.EventSourcing.MongoDB/README.md): the shared event
store contract on a MongoDB replica set and on a standalone server.

## Test framework

xUnit + FluentAssertions.

## Running tests

```sh
BIRKO_MONGO_HOST=localhost BIRKO_MONGO_STANDALONE_URL=mongodb://localhost:27018/ dotnet test --nologo
```

Without a variable, its tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../../LICENSE).
