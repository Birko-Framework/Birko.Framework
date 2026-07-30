---
area: repository-contract
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.Core/ViewModels/AbstractLogViewModel.cs
  - ../Birko.Data.Core/ViewModels/LogViewModel.cs
  - ../Birko.Data.Core/ViewModels/ModelViewModel.cs
  - ../Birko.Data.Core/ViewModels/ViewModel.cs
  - ../Birko.Data.Repositories/AbstractAsyncBulkRepository.cs
  - ../Birko.Data.Repositories/AbstractAsyncRepository.cs
  - ../Birko.Data.Repositories/AbstractBulkRepository.cs
  - ../Birko.Data.Repositories/AbstractRepository.cs
  - ../Birko.Data.Repositories/IAsyncBulkRepository.cs
  - ../Birko.Data.Repositories/IAsyncRepository.cs
  - ../Birko.Data.Repositories/IBaseRepository.cs
  - ../Birko.Data.Repositories/IBulkRepository.cs
  - ../Birko.Data.Repositories/IRepository.cs
  - ../Birko.Data.Repositories/RepositoryLocator.cs
  - ../Birko.Data.Repositories/ServiceCollectionExtensions.cs
  - ../Birko.Data.SQL.ViewModel/Extensions/IDataBaseRepository.cs
  - ../Birko.Data.SQL.ViewModel/Repositories/AsyncDataBaseRepository.cs
  - ../Birko.Data.SQL.ViewModel/Repositories/DataBaseRepository.cs
  - ../Birko.Data.SQL.ViewModel/Repositories/IDataBaseRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractAsyncBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractAsyncViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IAsyncBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IAsyncViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IViewModelRepository.cs
shaped-by: []
---

# Repository and ViewModel-repository abstractions

## Purpose

The repository layer is the application-facing façade over Birko's store hierarchy. It comes in two
distinct families that share nothing but the `IBaseRepository`/`IAsyncBaseRepository` lifecycle
interfaces and the `Birko.Data.Repositories` namespace.

The **model repository** family (`Birko.Data.Repositories`) is a thin pass-through: a repository holds
one store and forwards `Read`/`Create`/`Update`/`Delete`/`Count`/`Save` to it, adding only null-store
tolerance and a `CreateInstance` fallback. The **ViewModel repository** family
(`Birko.Data.ViewModel`, plus the SQL specialisation in `Birko.Data.SQL.ViewModel`) sits over the same
stores but presents a *presentation* type (`TViewModel`) instead of the persisted `TModel`. It owns the
ViewModel↔Model mapping, a SHA-256 hash-based change tracker that suppresses no-op updates, a
`ReadMode` switch that turns the repository read-only, and a filter contract expressed as
`IFilter<TModel>` rather than a raw LINQ expression.

Consumers reach repositories either directly (constructed with a store), through the process-wide
`RepositoryLocator` cache, or through the `AddRepository*` DI registration extensions. The
`Birko.Data.Core/ViewModels` classes supply the `INotifyPropertyChanged` ViewModel base types
(`ViewModel` → `ModelViewModel` → `LogViewModel`, and the parallel `AbstractLogViewModel`) that the
ViewModel repositories bind to via `ILoadable<T>`.

## Requirements

### Requirement: Model repository interface composition

The system SHALL compose the model-repository contract from single-responsibility interfaces —
`ICountRepository<T>`, `IReadRepository<T>`, `ICreateRepository<T>`, `IUpdateRepository<T>`,
`IDeleteRepository<T>` plus `IBaseRepository` — aggregated by `IRepository<T>`, which additionally
declares `CreateInstance()` and `Save(T)`; and SHALL mirror the same decomposition asynchronously in
`IAsyncRepository<T>` with `CancellationToken ct = default` on every operation and `SaveAsync`
returning the saved entity's `Guid`.

#### Scenario: Sync and async surfaces are symmetric

- **Given** `IRepository<T>` and `IAsyncRepository<T>`
- **When** their members are compared
- **Then** each sync member has an async counterpart (`Read`/`ReadAsync`, `Count`/`CountAsync`,
  `Create`/`CreateAsync`, `Update`/`UpdateAsync`, `Delete`/`DeleteAsync`, `Save`/`SaveAsync`,
  `Destroy`/`DestroyAsync`), and `CreateInstance()` is synchronous on both

#### Scenario: Entity type is constrained to AbstractModel

- **Given** an attempt to close `IRepository<T>` over a type that does not inherit
  `Birko.Data.Models.AbstractModel`
- **When** the code is compiled
- **Then** the `where T : Models.AbstractModel` constraint rejects it

#### Scenario: Repository operations expose no store delegate hook

- **Given** the store contract `ICreateStore<T>.Create(T data, StoreDataDelegate<T>? storeDelegate = null)`
- **When** `IRepository<T>.Create(T data)` is inspected
- **Then** the model repository surface has no `StoreDataDelegate<T>` parameter, so a per-item store
  callback cannot be supplied through a plain model repository (only the ViewModel repositories accept
  a delegate, as `ProcessDataDelegate<TModel>`)

### Requirement: Model repository delegates to its store and tolerates a null store

The system SHALL forward every `AbstractRepository<T>` / `AbstractAsyncRepository<T>` operation to the
injected store, and when the store is `null` SHALL return a neutral value instead of throwing:
`default`/`null` for reads, `Guid.Empty` for `Create` and `Save`, `0` for `Count`, and a silent no-op
for `Update`, `Delete` and `Destroy`.

#### Scenario: Read forwards to the store

- **Given** an `AbstractRepository<T>` subclass constructed with a non-null `IStore<T>`
- **When** `Read(guid)` is called
- **Then** the call is forwarded as `Store.Read(guid)` and its result returned unchanged

#### Scenario: Null store makes Create silently succeed with an empty Guid

- **Given** an `AbstractRepository<T>` subclass constructed with `store: null`
- **When** `Create(data)` is called
- **Then** no exception is thrown and `Guid.Empty` is returned, with nothing persisted

#### Scenario: Null store makes Count report zero

- **Given** an `AbstractAsyncRepository<T>` subclass constructed with `store: null`
- **When** `await CountAsync()` is awaited
- **Then** it completes with `0`

#### Scenario: Null store makes Destroy a no-op

- **Given** an `AbstractRepository<T>` with `Store == null`
- **When** `Destroy()` is called
- **Then** the method returns without effect (`Store?.Destroy()`)

### Requirement: CreateInstance falls back to Activator when no store is present

The system SHALL satisfy `CreateInstance()` from `Store.CreateInstance()` when a store is present, and
SHALL fall back to `Activator.CreateInstance<T>()` when `Store` is `null`.

#### Scenario: Store supplies the instance

- **Given** a repository whose `Store` is non-null
- **When** `CreateInstance()` is called
- **Then** `Store.CreateInstance()` is returned

#### Scenario: Activator supplies the instance

- **Given** a repository whose `Store` is `null`
- **When** `CreateInstance()` is called
- **Then** `Activator.CreateInstance<T>()` is returned, which throws if `T` has no public parameterless
  constructor

### Requirement: Bulk model repository requires a bulk store, and the sync and async families disagree on the failure mode

The system SHALL require the injected store to be an `IBulkStore<T>` / `IAsyncBulkStore<T>` for bulk
operations, but SHALL react differently per family: `AbstractBulkRepository<T>` throws
`InvalidOperationException("Store is not type of …")` on every bulk method when the store is not an
`IBulkStore<T>`, whereas `AbstractAsyncBulkRepository<T>` resolves `BulkStore` as
`Store as IAsyncBulkStore<T>` and, when that yields `null`, silently returns `Array.Empty<T>()` from
the collection reads, `null` from `ReadFirstAsync`, and completes the bulk writes as no-ops.

#### Scenario: Sync bulk read against a non-bulk store throws

- **Given** an `AbstractBulkRepository<T>` whose `Store` is an `IStore<T>` that does not implement
  `IBulkStore<T>`
- **When** `Read()` is called
- **Then** an `InvalidOperationException` whose message contains `Store is not type of` is thrown

#### Scenario: Async bulk read against a non-bulk store silently returns nothing

- **Given** an `AbstractAsyncBulkRepository<T>` constructed with an `IAsyncStore<T>` that does not
  implement `IAsyncBulkStore<T>`
- **When** `await ReadAsync()` is awaited
- **Then** an empty `IEnumerable<T>` (`Array.Empty<T>()`) is returned and no exception is thrown

#### Scenario: Async bulk delete-by-filter against a non-bulk store silently discards the request

- **Given** an `AbstractAsyncBulkRepository<T>` whose `BulkStore` resolves to `null`
- **When** `await DeleteAsync(x => x.Guid == someGuid)` is awaited
- **Then** the task completes successfully, nothing is deleted, and the caller receives no signal that
  the delete was dropped

#### Scenario: Sync bulk filter-update against a non-bulk store throws before touching data

- **Given** an `AbstractBulkRepository<T>` whose `Store` is not an `IBulkStore<T>`
- **When** `Update(filter, new PropertyUpdate<T>())` is called
- **Then** `InvalidOperationException` is thrown and no store call is attempted

### Requirement: On a bulk repository the filtered Read returns a collection and ReadFirst is the single-result accessor

The system SHALL declare, on `IBulkReadRepository<T>` / `IAsyncBulkReadRepository<T>`, a
`Read(filter, orderBy, limit, offset)` collection overload that hides the inherited single-result
`Read(filter)` from member lookup, and SHALL provide `ReadFirst(filter)` / `ReadFirstAsync(filter, ct)`
as the single-result accessor, forwarding to `IBulkStore<T>.ReadFirst` /
`IAsyncBulkStore<T>.ReadFirstAsync`.

#### Scenario: Filtered read on a bulk repository yields a collection

- **Given** a variable typed as `IBulkRepository<T>`
- **When** `Read(x => x.Guid != null)` is invoked
- **Then** the most-derived declaration wins and an `IEnumerable<T>` is returned, not a single `T?`

#### Scenario: ReadFirst yields at most one entity

- **Given** an `AbstractBulkRepository<T>` over a store containing three matching rows
- **When** `ReadFirst(filter)` is called
- **Then** `bulkStore.ReadFirst(filter)` is returned — a single `T?`, `null` when nothing matches

#### Scenario: Async bulk read supports sorting and paging

- **Given** an `AbstractAsyncBulkRepository<T>` with a bulk store
- **When** `await ReadAsync(filter, orderBy, limit: 10, offset: 20, ct)` is awaited
- **Then** all five arguments are forwarded verbatim to `BulkStore.ReadAsync(filter, orderBy, limit, offset, ct)`

### Requirement: Bulk repositories must not destroy their store twice

The system SHALL NOT override `Destroy`/`DestroyAsync` in the bulk repository subclasses, because
`BulkStore` is the same instance as `Store` (`Store as IAsyncBulkStore<T>`) and the base
implementation already destroys it.

#### Scenario: DestroyAsync destroys the single store once

- **Given** an `AbstractAsyncBulkRepository<T>` whose store's `DestroyAsync` is not idempotent
- **When** `await DestroyAsync()` is awaited
- **Then** the base `AbstractAsyncRepository.DestroyAsync` calls `Store.DestroyAsync(ct)` exactly once
  and no second call is made through `BulkStore`

#### Scenario: The same invariant holds for the ViewModel bulk repository

- **Given** an `AbstractAsyncBulkViewModelRepository<TViewModel, TModel>`
- **When** `await DestroyAsync()` is awaited
- **Then** only the base `AbstractAsyncViewModelRepository.DestroyAsync` runs, calling
  `Store.DestroyAsync(ct)` once

### Requirement: Destroy forwards to the store, which for the SQL family drops the table

The system SHALL implement repository `Destroy()`/`DestroyAsync(ct)` as a forward to the injected store's
`Destroy()` / `DestroyAsync(ct)` in every family (`AbstractRepository`, `AbstractAsyncRepository`,
`AbstractViewModelRepository`, `AbstractAsyncViewModelRepository`); for a SQL-backed store that call is
`DataBaseStore.Destroy()` → `Connector?.DropTable(new[] { typeof(T) })`, so the call deletes the entity's
table and every row in it. `IBaseRepository.Destroy` documents the member only as "Destroys the
repository and releases all resources"; the resource-release wording and the implemented table drop
disagree, and no repository-layer documentation records that data is destroyed.

#### Scenario: Destroying a SQL repository drops its table

- **Given** a `DataBaseRepository` over a `DataBaseBulkStore<SqLiteConnector, TModel>` holding rows
- **When** `Destroy()` is called
- **Then** `Store.Destroy()` executes `Connector.DropTable(new[] { typeof(TModel) })` and the table and
  all its rows are gone

#### Scenario: Locator eviction runs the same destructive call

- **Given** a repository cached in `RepositoryLocator`
- **When** `Destroy<TRepository>(key)` removes it from the cache and then calls `repository.Destroy()`
- **Then** the eviction also destroys the underlying store — for the SQL family, dropping the table —
  although the locator API is documented as destroying a cached repository

### Requirement: RepositoryLocator caches repositories per key and per repository type

The system SHALL maintain a static two-level cache `key → (repository type → instance)` guarded by a
single lock, SHALL construct a missing repository via `Activator.CreateInstance(type, store)`, and
SHALL return the cached instance on every subsequent call for the same key and type — performing both
the miss-check and the read inside the lock.

#### Scenario: Second call returns the cached instance

- **Given** `RepositoryLocator.GetRepository<TStore, TRepository>(store)` has been called once
- **When** it is called again with the same type arguments
- **Then** the identical repository instance is returned and no new instance is constructed

#### Scenario: Store-keyed overload defaults the key to the store type name

- **Given** `key` is not supplied to `GetRepository<TStore, TRepository>(store, key)`
- **When** the cache key is computed
- **Then** it is `typeof(TStore).FullName ?? string.Empty`

#### Scenario: Factory overload defaults the key to the repository type name

- **Given** `key` is not supplied to `GetRepository<TRepository>(Func<IBaseStore> storeFactory, key)`
- **When** the cache key is computed
- **Then** it is `typeof(TRepository).FullName ?? string.Empty`

#### Scenario: The store factory is invoked only on a cache miss

- **Given** a `GetRepository<TRepository>(storeFactory)` call that hits the cache
- **When** the method runs
- **Then** `storeFactory()` is not invoked and the cached repository (built with the earlier store) is
  returned

#### Scenario: A different store instance of the same type is ignored on a cache hit

- **Given** `GetRepository<TStore, TRepository>(storeA)` has cached a repository under
  `typeof(TStore).FullName`
- **When** `GetRepository<TStore, TRepository>(storeB)` is called with a different instance of the same
  store type and no explicit key
- **Then** the cached repository built around `storeA` is returned and `storeB` is silently discarded

#### Scenario: Concurrent creation is serialised

- **Given** two threads calling `GetRepository<TStore, TRepository>(store)` simultaneously for an
  uncached type
- **When** both execute
- **Then** the `lock (_lockObject)` guarantees exactly one `Activator.CreateInstance` call and both
  threads receive the same instance

### Requirement: The settings-keyed locator overload uses the settings only as a cache key

The system SHALL treat `GetRepository<TRepository, TSettings>(settings)` as a *keyed parameterless*
construction: the key is `settings?.GetId() ?? string.Empty`, the repository is created with
`Activator.CreateInstance(type)` and the settings are never applied to the repository; and SHALL
translate the resulting `MissingMethodException` into an `InvalidOperationException` naming the
repository type and pointing at the store-injecting overloads.

#### Scenario: Settings are not applied to the repository

- **Given** a repository type with a public parameterless constructor
- **When** `GetRepository<TRepository, TSettings>(settings)` is called
- **Then** the repository is constructed with no arguments and no `SetSettings`-style call is made with
  `settings`

#### Scenario: A store-only constructor produces a diagnosable failure

- **Given** a repository type whose only public constructor takes a store
- **When** `GetRepository<TRepository, TSettings>(settings)` is called
- **Then** `Activator.CreateInstance(type)` throws `MissingMethodException`, which is caught and
  rethrown as `InvalidOperationException` containing
  `has no public parameterless constructor` and the repository's `FullName`

#### Scenario: Null settings degrade to the empty key

- **Given** `settings` is `null`
- **When** the cache key is computed
- **Then** it is `string.Empty` and all such calls share one cache bucket

### Requirement: RepositoryLocator.Destroy removes the entry under the lock and disposes outside it

The system SHALL, in `Destroy<TRepository>(key)`, resolve the key as
`key ?? typeof(TRepository).FullName ?? string.Empty`, capture and remove the cached repository inside
the lock, prune the key bucket when it becomes empty, and call `repository.Destroy()` *after*
releasing the lock; and SHALL return silently when the cache, the key or the type is absent.

#### Scenario: Destroy removes then destroys

- **Given** a repository cached under key `"K"` for type `TRepository`
- **When** `Destroy<TRepository>("K")` is called
- **Then** the entry is removed from the dictionary while the lock is held, the empty `"K"` bucket is
  removed too, and `Destroy()` is invoked on the captured repository after the lock is released

#### Scenario: Unknown key is a no-op

- **Given** the cache holds no entry for the resolved key
- **When** `Destroy<TRepository>()` is called
- **Then** the method returns without throwing and without calling any `Destroy()`

#### Scenario: Default keys do not line up between creation and destruction

- **Given** a repository created by `GetRepository<TStore, TRepository>(store)` with no explicit key,
  therefore cached under `typeof(TStore).FullName`
- **When** `Destroy<TRepository>()` is called with no explicit key, resolving to
  `typeof(TRepository).FullName`
- **Then** no entry is found, nothing is destroyed and the cached repository remains for the process
  lifetime

#### Scenario: Settings-keyed destroy mirrors the settings-keyed creation

- **Given** a repository cached via `GetRepository<TRepository, TSettings>(settings)`
- **When** `Destroy<TRepository, TSettings>(settings)` is called
- **Then** it delegates to `Destroy<TRepository>(settings?.GetId())`, matching the creation key

### Requirement: DI registration builds repositories by reflection around a store service

The system SHALL provide `AddRepository<TStore, TRepository>(lifetime)` that registers `TStore` as
itself with the given lifetime and registers `TRepository` via a factory which resolves
`sp.GetRequiredService<TStore>()` and calls `Activator.CreateInstance(typeof(TRepository), store)`,
throwing `InvalidOperationException($"Failed to create instance of {name}")` when the cast to
`TRepository` yields `null`; SHALL default the lifetime to `ServiceLifetime.Scoped`; and SHALL expose
`AddRepositorySingleton`/`AddRepositoryScoped`/`AddRepositoryTransient` as lifetime-fixed aliases plus
overloads taking a pre-built store instance or a `Func<IServiceProvider, IBaseStore>` factory.

#### Scenario: Store and repository are both registered

- **Given** an empty `IServiceCollection`
- **When** `services.AddRepository<MyStore, MyRepository>()` is called
- **Then** two `ServiceDescriptor`s are added — `MyStore→MyStore` and a factory for `MyRepository` —
  both with `ServiceLifetime.Scoped`

#### Scenario: The repository is constructed reflectively, not by DI constructor injection

- **Given** `MyRepository` has a constructor `(IStore<T> store, ISerializer serializer)`
- **When** the registered factory runs
- **Then** `Activator.CreateInstance(typeof(MyRepository), store)` is attempted with the store as the
  only argument, so the second dependency is not injected from the container

#### Scenario: A shared store instance is captured for every resolution

- **Given** `services.AddRepository<MyRepository>(storeInstance, ServiceLifetime.Transient)`
- **When** the repository is resolved from two different scopes
- **Then** both repositories are constructed around the same captured `storeInstance`, regardless of
  the requested lifetime

#### Scenario: Repeated registration duplicates descriptors

- **Given** `AddRepository<MyStore, MyRepository>()` is called twice
- **When** the descriptors are inspected
- **Then** four descriptors exist, because registration uses `services.Add(...)` and not a `TryAdd`
  variant

#### Scenario: Only the concrete store type is registered

- **Given** `AddRepository<MyStore, MyRepository>()`
- **When** `IStore<T>` is requested from the provider
- **Then** resolution fails, because the store was registered under `typeof(MyStore)` only

### Requirement: ViewModel repository interface composition and filter contract

The system SHALL express the ViewModel-repository contract in terms of `IFilter<TModel>` (whose
`Filter()` yields `Expression<Func<TModel, bool>>?`) rather than a raw expression, SHALL constrain
`TViewModel : Models.ILoadable<TModel>` and `TModel : Models.AbstractModel`, and SHALL declare
`CreateInstance()` (ViewModel) alongside `CreateModelInstance()` (Model) on both
`IViewModelRepository<T, TModel>` and `IAsyncViewModelRepository<TViewModel, TModel>`; `ReadMode` SHALL
be declared on the async interface only.

#### Scenario: Filters are translated at the boundary

- **Given** an `IFilter<TModel>` implementation
- **When** `Read(filter)` or `CountAsync(filter, ct)` runs
- **Then** the repository passes `filter?.Filter()` to the store, so a `null` filter and a filter whose
  `Filter()` returns `null` are indistinguishable to the store

#### Scenario: ReadMode is reachable only through the async interface

- **Given** a caller holding `IViewModelRepository<TViewModel, TModel>`
- **When** it looks for `ReadMode`
- **Then** the member is absent from the sync interface (it exists as a public virtual property on
  `AbstractViewModelRepository` and is declared only on `IAsyncViewModelRepository`), so the caller
  cannot toggle the flag that the sync write methods enforce

#### Scenario: Two instance factories exist side by side

- **Given** an `AbstractViewModelRepository<TViewModel, TModel>`
- **When** `CreateInstance()` and `CreateModelInstance()` are called
- **Then** the first returns `(TViewModel)Activator.CreateInstance(typeof(TViewModel), Array.Empty<object>())!`
  and the second returns `Store.CreateInstance()`, or `Activator.CreateInstance<TModel>()` when
  `Store` is `null`

### Requirement: ViewModel↔Model mapping is repository-owned

The system SHALL require each concrete ViewModel repository to implement
`protected abstract void MapToModel(TViewModel source, TModel target)`, SHALL build a Model from a
ViewModel through `LoadModelInstance` (create a fresh model, then `MapToModel`), and SHALL build a
ViewModel from a Model through `LoadInstance` (create a fresh ViewModel, `result.LoadFrom(model)`, then
`StoreHash(model)`), returning `default` when the supplied model is `null`.

#### Scenario: Reading maps model to viewmodel and seeds change tracking

- **Given** a store that returns a non-null `TModel`
- **When** `Read(filter)` runs
- **Then** a new `TViewModel` is created, `LoadFrom(model)` is invoked on it, the model's hash is
  recorded, and the ViewModel is returned

#### Scenario: A null model yields a null viewmodel

- **Given** `Store.Read(...)` returns `null`
- **When** `LoadInstance(null)` runs
- **Then** `default` is returned and no hash is stored

#### Scenario: Writing maps viewmodel to a fresh model

- **Given** a `TViewModel` carrying edited values
- **When** `Update(data)` runs
- **Then** `LoadModelInstance(data)` creates a new `TModel` via `CreateModelInstance()` and populates it
  through `MapToModel`, so the model handed to the store is never the caller's instance

#### Scenario: The viewmodel is refreshed from the persisted model

- **Given** a successful `Create(data)` or `Update(data)`
- **When** the store call returns
- **Then** `data.LoadFrom(item)` is executed, propagating store-assigned values (e.g. `Guid`) back into
  the caller's ViewModel

#### Scenario: Delete does not guard against a null viewmodel

- **Given** `Delete(null)` on an `AbstractViewModelRepository` with a non-null store and `ReadMode == false`
- **When** the call runs
- **Then** control reaches `LoadModelInstance(null)` → `MapToModel(null, target)`, unlike `Create` and
  `Update` which return early on `data == null`

### Requirement: ViewModel repositories compute a SHA-256 model hash whose no-op verdict never reaches the store

The system SHALL maintain `IDictionary<Guid, byte[]> _modelHash` keyed by the model's `Guid`, SHALL
populate it from `StoreHash` (only when `ReadMode` is false and `data.Guid.HasValue`) using
`StringHelper.CalculateSHA256Hash(Serializer.Serialize(data))`, and SHALL, on `Update`, return `null!`
from the store delegate when `CheckHashChange` finds the freshly computed hash equal to the stored one,
while still refreshing the stored hash. That `null!` is intended as a skip signal, but
`StoreDataDelegate<T>`'s return value is read by no backend — every store calls
`storeDelegate?.Invoke(data)` and discards the result, then writes `data` regardless — so the write is
not suppressed.

#### Scenario: An unchanged entity is written anyway

- **Given** an entity read through the repository (hash recorded) and re-submitted unmodified
- **When** `Update(data)` runs
- **Then** `CheckHashChange` returns `false` and the delegate handed to `Store.Update` returns `null!`,
  but the store discarded that return value and persists the model regardless

#### Scenario: A changed entity is written

- **Given** an entity read through the repository and then mutated
- **When** `Update(data)` runs
- **Then** the recomputed hash differs, `CheckHashChange` returns `true`, and the model instance is
  returned from the delegate for the store to persist

#### Scenario: An untracked entity is always considered changed

- **Given** a model whose `Guid` is absent from `_modelHash` (or is `null`)
- **When** `CheckHashChange(data)` runs
- **Then** it returns `true`, so the update proceeds

#### Scenario: The serializer is pluggable with a JSON default

- **Given** `AbstractViewModelRepository(store, serializer: null)`
- **When** hashes are computed
- **Then** `Serializer` is a `SystemJsonSerializer`; supplying an `ISerializer` replaces it and changes
  the hash input

#### Scenario: Change tracking survives a bulk read

- **Given** an `AbstractAsyncBulkViewModelRepository` and a bulk `ReadAsync(filter, limit, offset, ct)`
- **When** each model is projected
- **Then** `LoadInstance(model)` is used, so `StoreHash` runs for every bulk-read entity and a later
  single-item `UpdateAsync` can still detect a no-op

#### Scenario: Bulk writes do not update the hash table

- **Given** a bulk `Create(data, processDelegate)` on `AbstractBulkViewModelRepository`
- **When** the models are projected via `LoadModelInstance`
- **Then** `StoreHash` is not called for them (unlike the single-item `Create`, whose store delegate
  calls `StoreHash(x)`), so the entities remain untracked until read back

### Requirement: ReadMode makes a ViewModel repository read-only and clears change tracking

The system SHALL, when `ReadMode` is set to `true`, clear `_modelHash` if it is non-empty, SHALL make
`StoreHash` and `RemoveHash` no-ops while it stays `true`, and SHALL throw
`InvalidOperationException("Repository is in Read Mode")` from every write entry point — single-item
`Create`/`Update`/`Delete` (sync and async) and every bulk create/update/delete overload, including the
filter-based ones — checking the flag *before* any store or null-argument check.

#### Scenario: Enabling read mode drops tracked hashes

- **Given** a repository with two tracked models
- **When** `ReadMode = true` is assigned
- **Then** `_modelHash.Clear()` runs and the count becomes zero

#### Scenario: Writes are rejected in read mode

- **Given** `ReadMode == true`
- **When** `Create(vm)`, `Update(vm)`, `Delete(vm)`, `Update(filter, action)`, `Update(filter, updates)`
  or `Delete(filter)` is called on the sync or async ViewModel repository
- **Then** `InvalidOperationException` with message `Repository is in Read Mode` is thrown

#### Scenario: The read-mode check precedes the store check

- **Given** `ReadMode == true` and `Store == null`
- **When** `CreateAsync(vm)` is awaited
- **Then** the exception is thrown rather than `Guid.Empty` being returned

#### Scenario: Reads remain available in read mode

- **Given** `ReadMode == true`
- **When** `Read(filter)` / `ReadAsync(filter, ct)` / `Count(filter)` is called
- **Then** the operation proceeds normally, but `LoadInstance` records no hash because `StoreHash`
  is gated on `!ReadMode`

#### Scenario: Plain model repositories have no read mode

- **Given** `AbstractRepository<T>` and `AbstractAsyncRepository<T>`
- **When** their members are inspected
- **Then** no `ReadMode` property or read-mode guard exists, so the non-ViewModel repositories can
  always write

### Requirement: ProcessDataDelegate is a transform whose result is honoured only on the sync bulk path

The system SHALL define `ProcessDataDelegate<TModel>` as `TModel (TModel data)` and SHALL apply it as
`item = processDelegate?.Invoke(item) ?? item`. On the bulk paths of the sync ViewModel repositories that
expression sits in the `data.Select(...)` projection handed to the store, so a returned replacement
instance is adopted; on the single-item paths it runs *inside* the `StoreDataDelegate<TModel>` given to
`Store.Create`/`Store.Update`, whose return value no backend reads, so a replacement instance is
discarded and the pre-transform model is persisted.

#### Scenario: A wrapping delegate result is adopted on the bulk path

- **Given** a `ProcessDataDelegate<TModel>` that returns a different model instance than it is given
- **When** `Create(IEnumerable<TViewModel> data, processDelegate)` runs
- **Then** the returned instance is the one enumerated into `IBulkStore<TModel>.Create`, not the
  original projection

#### Scenario: A delegate returning null falls back to the original

- **Given** a `ProcessDataDelegate<TModel>` returning `null`
- **When** `item = processDelegate?.Invoke(item) ?? item` executes
- **Then** the pre-delegate `item` is used

#### Scenario: The single-item create path also stores the hash inside the delegate

- **Given** a single-item `Create(vm, processDelegate)`
- **When** the store invokes the repository's `StoreDataDelegate`
- **Then** the process delegate runs first and `StoreHash(x)` is called on its result before the model
  is returned to the store

#### Scenario: A single-item replacement instance never reaches storage

- **Given** a `ProcessDataDelegate<TModel>` returning a different model instance, passed to the
  single-item `Create(vm, processDelegate)`
- **When** the store invokes the repository's `StoreDataDelegate`
- **Then** the replacement is hashed and returned from the delegate, but the store discards that return
  value and persists the `item` produced by `LoadModelInstance`, so the transform is lost

#### Scenario: The async bulk path takes a store delegate instead

- **Given** `IAsyncBulkViewModelCreateRepository<TViewModel, TModel>.CreateAsync`
- **When** its signature is inspected
- **Then** it accepts `Stores.StoreDataDelegate<TModel>?` (forwarded straight to the store) rather than
  `ProcessDataDelegate<TModel>?`, diverging from its sync sibling

### Requirement: Bulk ViewModel repositories require a bulk store, throwing ArgumentException — lazily on the read path

The system SHALL check `Store is not IBulkStore<TModel>` in every `AbstractBulkViewModelRepository`
method and throw `ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}")`; and because
`Read(filter, limit, offset)` is a `yield return` iterator, that check SHALL execute only when the
returned sequence is first enumerated, not when `Read` is called.

#### Scenario: Bulk create against a non-bulk store throws ArgumentException

- **Given** an `AbstractBulkViewModelRepository` whose `Store` is a plain `IStore<TModel>`
- **When** `Create(items)` is called with `ReadMode == false`
- **Then** `ArgumentException` is thrown (not the `InvalidOperationException` used by the non-ViewModel
  `AbstractBulkRepository` for the same condition)

#### Scenario: The read path defers the exception to enumeration

- **Given** the same repository
- **When** `var seq = Read(filter, 10, 0);` executes
- **Then** no exception is raised; the `ArgumentException` surfaces on the first `MoveNext()` of `seq`

#### Scenario: A null store also fails the check

- **Given** `Store == null`
- **When** any bulk ViewModel method runs
- **Then** `null is not IBulkStore<TModel>` holds and `ArgumentException` is thrown

#### Scenario: Bulk read projects each model and drops nulls

- **Given** a bulk store returning three models
- **When** `Read(filter, limit, offset)` is enumerated
- **Then** `((IBulkStore<TModel>)Store).Read(filter?.Filter(), null, limit, offset)` is called — always
  with `null` for `orderBy`, which the ViewModel bulk contract cannot express — and each non-null
  `LoadInstance(item)` is yielded

### Requirement: Async bulk ViewModel repository streams results and no-ops without a bulk store

The system SHALL expose the async bulk read as
`IAsyncEnumerable<TViewModel> ReadAsync(Expression<Func<TModel, bool>>? filter, int? limit, int? offset, CancellationToken ct)`
with `[EnumeratorCancellation]`, SHALL `yield break` when `BulkStore` is `null`, SHALL await the whole
underlying `BulkStore.ReadAsync(...)` result before yielding, and SHALL return early (no exception)
from every async bulk write when `BulkStore` or `data` is `null`.

#### Scenario: Missing bulk store yields an empty stream

- **Given** an `AbstractAsyncBulkViewModelRepository` whose `Store` does not implement
  `IAsyncBulkStore<TModel>`
- **When** the `IAsyncEnumerable<TViewModel>` from `ReadAsync(...)` is enumerated
- **Then** it completes immediately with no elements

#### Scenario: The async bulk read takes a raw expression, not IFilter

- **Given** `IAsyncBulkViewModelReadRepository<TViewModel, TModel>`
- **When** its read signature is compared with the sync
  `IBulkViewModelReadRepository<T, TModel>.Read(IFilter<TModel>?, int?, int?)`
- **Then** the async one accepts `Expression<Func<TModel, bool>>?` while the sync one accepts
  `IFilter<TModel>?`, so the two families are not interchangeable

#### Scenario: Null data on an async bulk write is silently ignored

- **Given** `ReadMode == false` and a valid `BulkStore`
- **When** `await CreateAsync((IEnumerable<TViewModel>)null!)` is awaited
- **Then** the method returns without calling the store and without throwing

#### Scenario: Models are projected lazily

- **Given** `UpdateAsync(IEnumerable<TViewModel> data, storeDelegate, ct)`
- **When** it runs
- **Then** `data.Select(LoadModelInstance)` is passed to `BulkStore.UpdateAsync` unmaterialised — no
  intermediate `ToList()` — and the store drives the enumeration

### Requirement: SQL ViewModel repositories validate the store type through wrappers and expose the connector

The system SHALL, in `DataBaseRepository<TConnector, TViewModel, TModel>` and
`AsyncDataBaseRepository<TConnector, TViewModel, TModel>`, reject a non-null store that fails
`IsStoreOfType<TModel, DataBaseBulkStore<TConnector, TModel>>` /
`IsStoreOfType<TModel, AsyncDataBaseBulkStore<TConnector, TModel>>` with `ArgumentException`
(paramName `store`), SHALL accept wrappers around such a store, and SHALL resolve `Connector` by
unwrapping (`GetUnwrappedStore<...>()?.Connector`), yielding `null` when the innermost store is not of
the expected concrete type.

#### Scenario: A tenant-wrapped database store is accepted by the constructor and then rejected by every bulk method

- **Given** a `DataBaseBulkStore<SqLiteConnector, TModel>` inside a `TenantStoreWrapper`
- **When** it is passed to the `DataBaseRepository` constructor
- **Then** `IsStoreOfType` succeeds through the wrapper and `Store` is assigned — but
  `TenantStoreWrapper<TStore, T>` implements only `IStore<T>`/`IStoreWrapper<T>`, while the inherited
  `AbstractBulkViewModelRepository` methods test the *outer* reference (`Store is not IBulkStore<TModel>`),
  so every bulk read/create/update/delete on the accepted repository throws `ArgumentException`; only the
  `IBulkStore<T>`-implementing `TenantBulkStoreWrapper` passes both checks

#### Scenario: An unrelated store is rejected

- **Given** an `InMemoryStore<TModel>`
- **When** it is passed to the `DataBaseRepository` constructor
- **Then** `ArgumentException` is thrown whose message begins
  `Store must be of type DataBaseBulkStore<TConnector, TModel> or a wrapper around it`

#### Scenario: The connector is reached through the wrapper chain

- **Given** a wrapped `AsyncDataBaseBulkStore<SqLiteConnector, TModel>`
- **When** `Connector` is read on `AsyncDataBaseRepository`
- **Then** the unwrapped `DataBaseStore.Connector` is returned; if unwrapping finds no store of that
  type, `Connector` is `null`

#### Scenario: The connector type parameter must be the concrete connector

- **Given** `AsyncDataBaseRepository<AbstractConnector, TViewModel, TModel>` and a real store typed
  `AsyncDataBaseBulkStore<SqLiteConnector, TModel>`
- **When** the constructor's `IsStoreOfType` check runs
- **Then** C# generic invariance makes the check fail and the constructor throws — which is why the
  repository is generic over `TConnector`

#### Scenario: Passing null leaves the repository with no store

- **Given** `new MyDataBaseRepository(store: null)` (explicit null through the store-taking constructor)
- **When** any operation runs
- **Then** the base constructor received `null` and did not substitute a default, so `Store` stays
  `null` and reads return `default`, `Count` returns `0`, `Create` returns `Guid.Empty` and writes are
  no-ops

#### Scenario: The parameterless constructor self-provisions a store

- **Given** `new MyDataBaseRepository()`
- **When** construction completes
- **Then** it chained to `this(new DataBaseBulkStore<TConnector, TModel>())`, so `Store` is that new
  store

### Requirement: SQL ViewModel repositories forward store init hooks

The system SHALL expose `AddOnInit(InitConnector)` / `RemoveOnInit(InitConnector)` on both the sync and
async SQL ViewModel repositories, forwarding to the unwrapped database store, and SHALL ignore a `null`
hook; only the sync family declares these members on an interface
(`IDataBaseRepository<TConnector, TViewModel, TModel>`, which also exposes `Connector`).

#### Scenario: The hook reaches the innermost store

- **Given** a wrapped database store
- **When** `AddOnInit(hook)` is called
- **Then** the hook is registered on the unwrapped `DataBaseBulkStore`/`AsyncDataBaseBulkStore`

#### Scenario: A null hook is ignored

- **Given** `AddOnInit(null)`
- **When** the call runs
- **Then** nothing is forwarded and no exception is thrown

#### Scenario: No store means no registration

- **Given** a repository whose unwrapping yields `null`
- **When** `RemoveOnInit(hook)` is called
- **Then** the null-conditional call performs no work

#### Scenario: There is no async counterpart interface

- **Given** `AsyncDataBaseRepository<TConnector, TViewModel, TModel>`
- **When** its declared base types are inspected
- **Then** it implements only `AbstractAsyncBulkViewModelRepository<TViewModel, TModel>` — no
  `IDataBaseRepository`-style async interface exists, so `Connector`, `DataBaseStore`, `AddOnInit` and
  `RemoveOnInit` are reachable only through the concrete class

### Requirement: The SQL ReadOne extension bypasses the repository and queries the connector directly

The system SHALL provide `IDataBaseRepositoryExtensions.ReadOne<TRepository, TConnector, TViewModel, TModel>`
which, when `repository.Connector` is non-null, calls
`Connector.Select<TModel, object>(typeof(TModel), filter?.Filter(), orderByExpr, 1, 0)`, returns
`repository.LoadInstance(firstItem)` for the first row, and returns `default` when the connector is
`null` or the query yields no rows.

#### Scenario: First row is mapped to a viewmodel

- **Given** a repository with a live connector and a filter matching two rows
- **When** `ReadOne(filter, orderByExpr)` is called
- **Then** the connector is queried with limit 1 / offset 0 and the first row is returned as a
  ViewModel via `LoadInstance` (which also records its hash)

#### Scenario: No connector yields default

- **Given** `repository.Connector == null`
- **When** `ReadOne(filter)` is called
- **Then** `default` is returned without querying anything

#### Scenario: No rows yield default

- **Given** a filter matching nothing
- **When** `ReadOne(filter)` is called
- **Then** the `foreach` body never executes and `default` is returned

#### Scenario: Ordering is expressible here but not on the repository read

- **Given** `orderByExpr` as `IDictionary<Expression<Func<TModel, object>>, bool>`
- **When** `ReadOne` runs
- **Then** the ordering is passed to the connector, whereas
  `AbstractBulkViewModelRepository.Read` always passes `null` for `orderBy`

### Requirement: ViewModel base classes raise PropertyChanged only on an actual change

The system SHALL derive all ViewModels from `ViewModel : INotifyPropertyChanged`, which exposes a
`PropertyChanged` event and a `protected RaisePropertyChanged([CallerMemberName] string propertyName = "")`
helper, and SHALL implement every setter in `ModelViewModel`, `LogViewModel` and
`AbstractLogViewModel` as a guarded assignment that raises the event only when the incoming value
differs from the current one.

#### Scenario: Assigning the same value raises nothing

- **Given** a `ModelViewModel` whose `Guid` is already `g`
- **When** `Guid = g` is assigned
- **Then** the `_guid != value` guard fails and `PropertyChanged` is not raised

#### Scenario: Assigning a new value raises the event with the declared name

- **Given** a `ModelViewModel` with `Guid == null`
- **When** `Guid = Guid.NewGuid()` is assigned
- **Then** `PropertyChanged` fires with `PropertyName == "Guid"` (the `GuidProperty` constant)

#### Scenario: Log timestamps default to construction time

- **Given** a newly constructed `LogViewModel` or `AbstractLogViewModel`
- **When** `CreatedAt` and `UpdatedAt` are read
- **Then** both hold the `DateTime.UtcNow` captured at field initialisation and `PrevUpdatedAt` is
  `null`

#### Scenario: The event is safe with no subscribers

- **Given** no handler attached to `PropertyChanged`
- **When** `RaisePropertyChanged()` runs
- **Then** the null-conditional invoke performs no work

### Requirement: ViewModel base classes load from entity interfaces without circular type references

The system SHALL make `ModelViewModel` implement `IGuidEntity`, `ILoadable<IGuidEntity>` and
`ILoadable<ModelViewModel>`; `LogViewModel : ModelViewModel` additionally implement `ILogEntity`,
`ILoadable<ILogEntity>` and `ILoadable<LogViewModel>` (chaining to `base.LoadFrom`); and
`AbstractLogViewModel : ViewModel` implement the same `ILogEntity` surface independently as a parallel
hierarchy with `virtual LoadFrom` overloads; and every `LoadFrom` SHALL no-op when passed `null`.

#### Scenario: Loading from a log entity copies identity and all three timestamps

- **Given** an `ILogEntity` with `Guid`, `CreatedAt`, `UpdatedAt` and `PrevUpdatedAt` set
- **When** `LogViewModel.LoadFrom(entity)` is called
- **Then** `base.LoadFrom` copies `Guid` and the derived method copies `CreatedAt`, `UpdatedAt` and
  `PrevUpdatedAt`, each raising `PropertyChanged` if changed

#### Scenario: Loading from null changes nothing

- **Given** any of the `LoadFrom` overloads
- **When** it is invoked with `null`
- **Then** the `if (data != null)` guard skips all assignments and no event is raised

#### Scenario: The two log hierarchies are independent

- **Given** `LogViewModel` and `AbstractLogViewModel`
- **When** their inheritance is compared
- **Then** `LogViewModel` extends `ModelViewModel` while `AbstractLogViewModel` extends `ViewModel`
  directly and re-declares `Guid`, the three timestamp properties and the
  `CreatedAtProperty`/`UpdatedAtProperty`/`PrevUpdatedAtProperty` constants; only
  `AbstractLogViewModel`'s `LoadFrom` overloads are `virtual`

#### Scenario: A viewmodel satisfies the repository's ILoadable constraint

- **Given** `AbstractViewModelRepository<TViewModel, TModel>` with `TViewModel : ILoadable<TModel>`
- **When** a `TModel` inheriting `AbstractModel` (an `IGuidEntity`) is loaded
- **Then** the concrete ViewModel must declare `ILoadable<TModel>` itself — the inherited
  `ILoadable<IGuidEntity>` / `ILoadable<ILogEntity>` implementations do not satisfy the constraint
