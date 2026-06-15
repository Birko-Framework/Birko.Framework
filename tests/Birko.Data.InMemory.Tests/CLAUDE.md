# Birko.Data.InMemory.Tests

## Scope
xUnit + FluentAssertions tests for `Birko.Data.InMemory` — the in-memory store backend.

## Conventions
- One test class per store: `InMemoryStoreTests` (sync `InMemoryStore<T>`), `AsyncInMemoryStoreTests` (async `AsyncInMemoryStore<T>`).
- `TestModel : AbstractModel` is the shared fixture entity.
- Cover both success and edge paths: CRUD round-trips, bulk operations, filter-based
  `Update`/`Delete`, ordering + paging, lazy-init (CRUD without explicit `Init`), aggregation,
  `Save` create-vs-update, `Destroy`, the settings-compatibility surface, and (async) cancellation.

## Running
`dotnet test C:\Source\Birko.Data.InMemory.Tests\Birko.Data.InMemory.Tests.csproj`
