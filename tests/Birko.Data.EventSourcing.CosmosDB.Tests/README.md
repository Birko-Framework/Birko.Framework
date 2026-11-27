# Birko.Data.EventSourcing.CosmosDB.Tests

Live tests for [Birko.Data.EventSourcing.CosmosDB](../../Birko.Data.EventSourcing.CosmosDB/README.md): the shared event
store contract and the Cosmos-specific rules, on the emulator or an account.

## Test framework

xUnit + FluentAssertions.

## Running tests

```sh
BIRKO_COSMOS_CONNECTION="AccountEndpoint=http://localhost:8081/;AccountKey=…" BIRKO_COSMOS_CONNECTION_MODE=Gateway dotnet test --nologo
```

Without `BIRKO_COSMOS_CONNECTION` the tests report SKIPPED. Set `BIRKO_REQUIRE_LIVE` to make that a failure.

## License

See the repository root [LICENSE](../../LICENSE).
