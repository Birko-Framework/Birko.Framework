# Birko.Data.TimescaleDB.ViewModel.Tests

Test project for `Birko.Data.TimescaleDB.ViewModel`.

## Scope
- `AsyncTimescaleDBRepository<TViewModel, TModel>` — constructor store-type validation (foreign store →
  `ArgumentException`), `SetSettings(TimescaleDBSettings/RemoteSettings)` routing (Connector becomes a
  `TimescaleDBConnector`), and the `InitAsync`/`DropAsync`/`CreateSchemaAsync`/`CreateHypertableAsync`
  "Connector not initialized" guards.

## Conventions
- xUnit + FluentAssertions; Moq for a foreign `IAsyncStore<T>`.
- Offline — connectors are constructed lazily, so no live TimescaleDB/PostgreSQL is needed.
