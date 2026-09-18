# Birko.Data.InMemory.Tests

Test suite for [Birko.Data.InMemory](../Birko.Data.InMemory). xUnit + FluentAssertions.

```
dotnet test
```

Covers `InMemoryStore<T>` and `AsyncInMemoryStore<T>`: CRUD, bulk operations, filter-based
update/delete, ordering/paging, lazy-init, aggregation, and cancellation.
