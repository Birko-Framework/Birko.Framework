# Birko.Security.OAuth.Server.StoreFactory.Tests

Tests for the OAuth server stores over any `IStoreFactory` backend (TASK-1013): `AddOAuthServerStores(factory)` and the
`Factory*Store` classes in `Birko.Security.OAuth.Server` (`Stores/Factory`).

## Test framework
xUnit + FluentAssertions.

## Running tests

```sh
dotnet test --nologo
```

Offline it runs the shared OAuth store contract on InMemory and SQLite, the refusal of JSON and XML, the DI
registration, and the read of a database the (deleted) SQLite OAuth package wrote, kept as a SQL dump. Set `BIRKO_PG_HOST`, `BIRKO_MYSQL_HOST`,
`BIRKO_MSSQL_HOST`, `BIRKO_MONGO_HOST`, `BIRKO_ES_URL`, `BIRKO_RAVEN_URL` or `BIRKO_COSMOS_CONNECTION` (images and
variables as in `.github/workflows/live-tests.yml`) to run the same contract against a live server;
`BIRKO_REQUIRE_LIVE=1` turns a missing one into a failure.

## License
Part of the Birko Framework.
