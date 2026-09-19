# Birko.Data.InMemory

## Overview
In-memory storage implementation for the Birko data layer. Backs the entity set with a
thread-safe `ConcurrentDictionary<Guid, T>` that lives for the lifetime of the store instance.

## Project Location
`Birko.Data.InMemory/`

## Purpose
- **Testing** — a single, correct, thread-safe store to use as a test double instead of
  hand-rolling per-project fakes. Implements the full `IBulkStore<T>` / `IAsyncBulkStore<T>`
  contract (filter-based `Update`/`Delete`, `ReadFirst`/`ReadFirstAsync`, aggregation), so a
  test exercises the same surface a real backend would.
- **Reference implementation** — the simplest possible store; mirrors the `AbstractJsonStore`
  dictionary model minus the file I/O.
- **Prototyping / demos** — run a Birko app or example with zero database or file setup.

No data is persisted: everything is lost when the store is garbage-collected or `Destroy()` /
`DestroyAsync()` is called.

## Components

### Stores
- `InMemoryStore<T>` — synchronous in-memory bulk store
- `AsyncInMemoryStore<T>` — asynchronous in-memory bulk store
- `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>` — base classes holding the
  `ConcurrentDictionary` + CRUD/aggregation logic; subclass these to customize behavior

All four follow the framework store hierarchy: concrete stores override the `*Core` methods
(`CreateCore`, `ReadCore`, `UpdateCore`, `DeleteCore`, `CountCore` + bulk variants), **never**
the public CRUD methods, so lazy-init via `EnsureInitialized`/`EnsureInitializedAsync` is
preserved. `Read(Guid)` / `ReadAsync(Guid)` and `Delete(filter)` / `DeleteAsync(filter)` are
overridden for O(1) lookup and single-pass deletion respectively.

## Settings
In-memory stores have no connection string or path. They implement
`ISettingsStore<Settings>` / `ISettingsStore<ISettings>` purely for drop-in compatibility —
so an `InMemoryStore<T>` can stand in for a `JsonStore<T>` / SQL store in the same wiring —
but the supplied `Settings` are retained and otherwise ignored.

## Thread safety
The backing store is a `ConcurrentDictionary`. Bulk reads materialize to a `List<T>` snapshot
before returning so callers never enumerate a live, concurrently-mutating collection.

## Usage

```csharp
var store = new AsyncInMemoryStore<MyModel>();
var id = await store.CreateAsync(new MyModel { Name = "x" });
var loaded = await store.ReadAsync(id);
var count = await store.CountAsync(m => m.Name == "x");
```

## Aggregation
Both stores implement `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` via the shared
`AggregateHelper.LinqAggregate(Async)` (LINQ over the in-memory values).

## Tests
`Birko.Data.InMemory.Tests` — xUnit + FluentAssertions. Covers CRUD round-trips, bulk
operations, filter-based update/delete, ordering/paging, lazy-init, aggregation, cancellation,
and the settings-compatibility surface.

## Maintenance
When changing the public API or behavior, update README.md and this CLAUDE.md.
