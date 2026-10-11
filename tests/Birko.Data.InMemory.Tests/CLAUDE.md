# Birko.Data.InMemory.Tests

## Scope
xUnit + FluentAssertions tests for `Birko.Data.InMemory` — the in-memory store backend.

## Conventions
- One test class per store: `InMemoryStoreTests` (sync `InMemoryStore<T>`), `AsyncInMemoryStoreTests` (async `AsyncInMemoryStore<T>`).
- `TestModel : AbstractModel` is the shared fixture entity.
- `StoreCrudContractTests` (TASK-163) pins where the in-memory store must behave like a real backend — create never
  replaces, an update cannot resurrect a deleted entity, `Delete(filter)` reaches `DeleteCore` — plus `StoreLocator`
  (async stores, colliding settings) and `IsStoreOfType` through a two-wrapper chain. `StoreLocator` is process-wide:
  every test there uses its own settings id.
- Cover both success and edge paths: CRUD round-trips, bulk operations, filter-based
  `Update`/`Delete`, ordering + paging, lazy-init (CRUD without explicit `Init`), aggregation,
  `Save` create-vs-update, `Destroy`, the settings-compatibility surface, and (async) cancellation.

## Running
`dotnet test tests/Birko.Data.InMemory.Tests/Birko.Data.InMemory.Tests.csproj`
