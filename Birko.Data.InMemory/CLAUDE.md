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
preserved. `Read(Guid)` / `ReadAsync(Guid)` is the one public override (O(1) lookup). `Delete(filter)` used to be
overridden too and bypassed `DeleteCore`; it is the base class's since TASK-163 (SH-M312).

### Behaves like a real backend, on purpose
It is the canonical test double, so where it is lenient a production bug passes the tests:

- **`Create` never replaces.** An existing key — or a key repeated inside one bulk batch — throws `StoreException`
  before anything is written, as a primary key would (SH-M310). Use `Update` to replace.
- **`Update` cannot resurrect a deleted entity.** The write is a `TryUpdate` against the stored value, so a delete
  that lands mid-update (another thread, or the store delegate itself) wins (SH-M308).
- Still lenient, tracked: an update of a missing key is a silent no-op on every backend ([[TASK-555]]); a
  `Guid.Empty` id is kept on create ([[TASK-556]]); the store keeps the caller's live object, not a snapshot
  ([[TASK-557]]).

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
