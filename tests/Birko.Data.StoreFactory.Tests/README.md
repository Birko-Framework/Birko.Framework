# Birko.Data.StoreFactory.Tests

Tests for the backend-neutral store factory (TASK-990): one `IStoreFactory` for SQL and NoSQL backends, chosen by a
backend name and a connection string, or by a settings object.

```sh
dotnet test --nologo
```

Offline it runs the store suite on InMemory, JSON, XML and SQLite, and checks backend choice and every backend's
connection-string parsing. Set `BIRKO_MONGO_HOST`, `BIRKO_ES_URL`, `BIRKO_RAVEN_URL` or `BIRKO_COSMOS_CONNECTION`
(images and variables as in `.github/workflows/live-tests.yml`) to run the same suite against a live server;
`BIRKO_REQUIRE_LIVE=1` turns a missing one into a failure.
