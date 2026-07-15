# Birko.Data.ViewModel.Tests

Test project for `Birko.Data.ViewModel` (the abstract ViewModel repository layer).

## Scope
- `BulkViewModelRepositoryReadModeTests` — CR-M180 (bulk Create/Update/Delete respect `ReadMode` on
  the sync + async bulk repositories) and CR-M179 (async bulk `ReadAsync` routes through
  `LoadInstance` so change-tracking is primed).
- `BulkViewModelRepositoryDelegateTests` — the bulk write paths forward the `StoreDataDelegate`.
- `BulkViewModelRepositoryDestroyTests` — the ViewModel analogue of CR-H080 (surfaced by the
  CR-L234 review): `AbstractAsyncBulkViewModelRepository` no longer overrides `DestroyAsync`
  (`BulkStore` is the same instance as `Store`, so the override destroyed one store twice); a
  counting store proves exactly one `DestroyAsync` per repository destroy + a structural
  no-override pin.

## Conventions
- xUnit + FluentAssertions; `Birko.Data.InMemory` stores as offline test doubles.
- Repos under test are private test subclasses supplying `MapToModel`.
