# Birko.Data.TimescaleDB.ViewModel.Tests

Test project for `Birko.Data.TimescaleDB.ViewModel`.

## Scope
- `AsyncTimescaleDBRepository<TViewModel, TModel>` — constructor store-type validation (foreign store →
  `ArgumentException`), `SetSettings(TimescaleDBSettings/RemoteSettings)` routing (Connector becomes a
  `TimescaleDBConnector`), and the `InitAsync`/`DropAsync`/`CreateSchemaAsync`/`CreateHypertableAsync`
  "Connector not initialized" guards (CR-L235: one `RequireConnector()` capture per call).
- CR-L234 — `DestroyAsync` is no longer overridden (the override double-dropped the table via the
  unwrapped connector, bypassing wrappers): structural no-override pin + behavioral change pin
  (an unconfigured repo's `DestroyAsync` used to throw `InvalidOperationException` from the trailing
  `DropAsync`, now completes quietly; `DropAsync` stays as the explicit schema-drop helper).

## Conventions
- xUnit + FluentAssertions; Moq for a foreign `IAsyncStore<T>`.
- Offline — connectors are constructed lazily, so no live TimescaleDB/PostgreSQL is needed.
