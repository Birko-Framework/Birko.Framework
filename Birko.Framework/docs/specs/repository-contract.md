---
area: repository-contract
generated-at: 4b5bf7e6a33b66aa46da5e6adc40ff88e547da43
generated-on: 2026-10-16
sources:
  - ../Birko.Data.Core/ViewModels/AbstractLogViewModel.cs
  - ../Birko.Data.Core/ViewModels/LogViewModel.cs
  - ../Birko.Data.Core/ViewModels/ModelViewModel.cs
  - ../Birko.Data.Core/ViewModels/ViewModel.cs
  - ../Birko.Data.CosmosDB/Repositories/AsyncCosmosDBModelRepository.cs
  - ../Birko.Data.CosmosDB/Repositories/CosmosDBModelRepository.cs
  - ../Birko.Data.InfluxDB/Repositories/AsyncInfluxDBModelRepository.cs
  - ../Birko.Data.InfluxDB/Repositories/AsyncInfluxDBRepository.cs
  - ../Birko.Data.InfluxDB/Repositories/InfluxDBModelRepository.cs
  - ../Birko.Data.InfluxDB/Repositories/InfluxDBRepository.cs
  - ../Birko.Data.MongoDB/Repositories/AsyncMongoDBModelRepository.cs
  - ../Birko.Data.MongoDB/Repositories/AsyncMongoDBRepository.cs
  - ../Birko.Data.MongoDB/Repositories/MongoDBModelRepository.cs
  - ../Birko.Data.MongoDB/Repositories/MongoDBRepository.cs
  - ../Birko.Data.RavenDB/Repositories/AsyncRavenDBModelRepository.cs
  - ../Birko.Data.RavenDB/Repositories/AsyncRavenDBRepository.cs
  - ../Birko.Data.RavenDB/Repositories/RavenDBModelRepository.cs
  - ../Birko.Data.RavenDB/Repositories/RavenDBRepository.cs
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
  - ../Birko.Data.SQL/Repositories/AsyncDataBaseModelRepository.cs
  - ../Birko.Data.SQL/Repositories/DataBaseModelRepository.cs
  - ../Birko.Data.SQL/Repositories/IDataBaseRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractAsyncBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractAsyncViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IAsyncBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IAsyncViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IBulkViewModelRepository.cs
  - ../Birko.Data.ViewModel/Repositories/IViewModelRepository.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
---
# Repository and ViewModel-repository abstractions

## Purpose

The repository layer is the application-facing façade over Birko's store hierarchy. It comes in two
distinct families that share nothing but the `IBaseRepository`/`IAsyncBaseRepository` lifecycle
interfaces and the `Birko.Data.Repositories` namespace.

The **model repository** family (`Birko.Data.Repositories`) is a thin pass-through: a repository holds
one store and forwards `Read`/`Create`/`Update`/`Delete`/`Count`/`Save` to it, adding only a
refusal when no store is present and a `CreateInstance` fallback. The **ViewModel repository** family
(`Birko.Data.ViewModel`, plus the SQL specialisation in `Birko.Data.SQL.ViewModel`) sits over the same
stores but presents a *presentation* type (`TViewModel`) instead of the persisted `TModel`. It owns the
ViewModel↔Model mapping, a SHA-256 hash-based change tracker (which does not gate writes), a
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

### Requirement: Model repository delegates to its store and refuses to operate without one

The system SHALL forward every `AbstractRepository<T>` / `AbstractAsyncRepository<T>` operation to the
injected store, and when the store is `null` SHALL throw the `InvalidOperationException` built by the
protected `NoStore()` helper (message `{GetType().Name} has no store; construct it with one (or let its
default constructor provide it).`) from every data operation — `Read`, `Create`, `Update`, `Delete`,
`Count` and `Save` on the sync repository; `ReadAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`,
`CountAsync` and `SaveAsync` on the async one — instead of answering `default`/`null`, `Guid.Empty` or `0`. `Destroy` / `DestroyAsync`
SHALL remain a silent no-op (there is nothing to destroy) and `CreateInstance()` SHALL keep its
`Activator` fallback. (TASK-646: the async `UpdateAsync` and `DeleteAsync` returned silently with no store,
the one gap TASK-162's fix left; measured, both now refuse like their sync twins.)

#### Scenario: Read forwards to the store

- **Given** an `AbstractRepository<T>` subclass constructed with a non-null `IStore<T>`
- **When** `Read(guid)` is called
- **Then** the call is forwarded as `Store.Read(guid)` and its result returned unchanged

#### Scenario: Null store makes Create throw

- **Given** an `AbstractRepository<T>` subclass constructed with `store: null`
- **When** `Create(data)` is called
- **Then** an `InvalidOperationException` containing `has no store` is thrown, with nothing persisted,
  rather than `Guid.Empty` being returned

#### Scenario: Null store makes Count throw

- **Given** an `AbstractAsyncRepository<T>` subclass constructed with `store: null`
- **When** `await CountAsync()` is awaited
- **Then** it faults with `InvalidOperationException` rather than completing with `0`

#### Scenario: Null store still makes the async Update and Delete silent no-ops

- **Given** an `AbstractAsyncRepository<T>` subclass constructed with `store: null`
- **When** `await UpdateAsync(data)` or `await DeleteAsync(data)` is awaited
- **Then** the task completes successfully with nothing written, while the sync `Update(data)` /
  `Delete(data)` on `AbstractRepository<T>` throw `InvalidOperationException` for the same condition

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

### Requirement: Bulk model repository requires a bulk store, and both families throw when it is missing

The system SHALL require the injected store to be an `IBulkStore<T>` / `IAsyncBulkStore<T>` for bulk
operations and SHALL throw `InvalidOperationException` from every bulk method in both families when it
is not: `AbstractBulkRepository<T>` throws `InvalidOperationException("Store is not type of …")` when
the store is not an `IBulkStore<T>` (a `null` store included), and `AbstractAsyncBulkRepository<T>`
resolves `BulkStore` as `Store as IAsyncBulkStore<T>` and, when that yields `null`, throws from the
collection reads, `ReadFirstAsync` and every bulk write — the `NoStore()` exception when `Store` itself
is `null`, otherwise `InvalidOperationException("Store is not type of …IAsyncBulkStore…")`.

#### Scenario: Sync bulk read against a non-bulk store throws

- **Given** an `AbstractBulkRepository<T>` whose `Store` is an `IStore<T>` that does not implement
  `IBulkStore<T>`
- **When** `Read()` is called
- **Then** an `InvalidOperationException` whose message contains `Store is not type of` is thrown

#### Scenario: Async bulk read against a non-bulk store throws

- **Given** an `AbstractAsyncBulkRepository<T>` constructed with an `IAsyncStore<T>` that does not
  implement `IAsyncBulkStore<T>`
- **When** `await ReadAsync()` is awaited
- **Then** an `InvalidOperationException` whose message contains `Store is not type of` is thrown,
  rather than an empty sequence being returned

#### Scenario: Async bulk delete-by-filter against a non-bulk store is refused

- **Given** an `AbstractAsyncBulkRepository<T>` whose `BulkStore` resolves to `null`
- **When** `await DeleteAsync(x => x.Guid == someGuid)` is awaited
- **Then** the task faults with `InvalidOperationException`, nothing is deleted, and a dropped delete
  can no longer be mistaken for one that matched nothing

#### Scenario: Async bulk repository with no store reports the missing store

- **Given** an `AbstractAsyncBulkRepository<T>` constructed with `store: null`
- **When** any bulk method is awaited
- **Then** the `NoStore()` exception (`… has no store …`) is thrown rather than the not-a-bulk-store one

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

#### Scenario: Several per-backend repositories still destroy their store twice

- **Given** a `MongoDBModelRepository`, `MongoDBRepository`, `AsyncMongoDBModelRepository`,
  `AsyncMongoDBRepository`, any of the four InfluxDB repositories, `AsyncRavenDBModelRepository`,
  `AsyncRavenDBRepository` or `AsyncCosmosDBModelRepository`
- **When** `Destroy()` / `await DestroyAsync()` is called
- **Then** the override first runs `base.Destroy()` / `base.DestroyAsync(ct)` — which destroys `Store` —
  and then destroys the unwrapped backend store (`MongoDBStore`, `InfluxDBStore`, `RavenDBStore`,
  `CosmosStore`) a second time, directly or through `Drop()` / `DropAsync(ct)`; when `Store` is that
  backend store undecorated, the same instance is destroyed twice. The sync `RavenDBModelRepository`,
  `RavenDBRepository` and `CosmosDBModelRepository` do not override and destroy once

### Requirement: Destroy forwards to the store, which for the SQL family drops the table

The system SHALL implement repository `Destroy()`/`DestroyAsync(ct)` as a forward to the injected store's
`Destroy()` / `DestroyAsync(ct)` in every family (`AbstractRepository`, `AbstractAsyncRepository`,
`AbstractViewModelRepository`, `AbstractAsyncViewModelRepository`); for a SQL-backed store that call is
`DataBaseStore.Destroy()` → `Connector?.DropTable(new[] { typeof(T) })`, so the call deletes the entity's
table and every row in it.

`IBaseRepository.Destroy` and `IAsyncBaseRepository.DestroyAsync(ct)` SHALL document that the member
**permanently deletes every row the underlying store can see and is not disposal**, SHALL give the
per-backend blast radius, and SHALL name the alternatives. SH-H046 (widened from the store contract):
both previously read *"destroys the repository and releases all resources"* — the same sentence as
`IBaseStore.Destroy()` — so the resource-release wording and the implemented table drop disagreed, and
no repository-layer documentation recorded that data is destroyed. Because every family forwards to the
store, this is not a milder operation one layer up but the same destruction one call away; documenting
a warning on the store contract while leaving a reassurance here would be a half-fix.

No type in this contract declares `IDisposable`, so `Destroy` was the only cleanup-looking member a
caller could find at the repository layer; the documentation redirects resource release to the
underlying store, where `RavenDBStore`, `InfluxDBStore` and the SQL stores do implement it.

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
the miss-check and the read inside the lock. Where the locator was handed a store (the store-keyed and
factory overloads), it SHALL record, by reference, the store each cached repository was built around,
and the store-keyed overload SHALL refuse a cache hit whose recorded store is a different instance.

#### Scenario: Second call returns the cached instance

- **Given** `RepositoryLocator.GetRepository<TStore, TRepository>(store)` has been called once
- **When** it is called again with the same type arguments
- **Then** the identical repository instance is returned and no new instance is constructed

#### Scenario: Store-keyed overload defaults the key to the repository type name

- **Given** `key` is not supplied to `GetRepository<TStore, TRepository>(store, key)`
- **When** the cache key is computed
- **Then** it is `typeof(TRepository).FullName ?? string.Empty`, the same default the factory overload
  and `Destroy<TRepository>()` use

#### Scenario: Factory overload defaults the key to the repository type name

- **Given** `key` is not supplied to `GetRepository<TRepository>(Func<IBaseStore> storeFactory, key)`
- **When** the cache key is computed
- **Then** it is `typeof(TRepository).FullName ?? string.Empty`

#### Scenario: The store factory is invoked only on a cache miss

- **Given** a `GetRepository<TRepository>(storeFactory)` call that hits the cache
- **When** the method runs
- **Then** `storeFactory()` is not invoked and the cached repository (built with the earlier store) is
  returned

#### Scenario: A different store instance under a cached key is refused

- **Given** `GetRepository<TStore, TRepository>(storeA)` has cached a repository under
  `typeof(TRepository).FullName`
- **When** `GetRepository<TStore, TRepository>(storeB)` is called with a different instance of the same
  store type and no explicit key
- **Then** an `InvalidOperationException` is thrown whose message says the repository is already cached
  under that key for a different store instance and to pass a key per store; passing `storeA` again
  returns the cached repository

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
releasing the lock; and SHALL return silently when the cache, the key or the type is absent. When the
destroyed repository has a recorded store, the system SHALL also evict, under the same lock, every other
cached repository recorded against that same store instance (pruning emptied buckets), without calling
`Destroy()` on them.

#### Scenario: Destroy removes then destroys

- **Given** a repository cached under key `"K"` for type `TRepository`
- **When** `Destroy<TRepository>("K")` is called
- **Then** the entry is removed from the dictionary while the lock is held, the empty `"K"` bucket is
  removed too, and `Destroy()` is invoked on the captured repository after the lock is released

#### Scenario: Unknown key is a no-op

- **Given** the cache holds no entry for the resolved key
- **When** `Destroy<TRepository>()` is called
- **Then** the method returns without throwing and without calling any `Destroy()`

#### Scenario: Default keys line up between creation and destruction

- **Given** a repository created by `GetRepository<TStore, TRepository>(store)` with no explicit key,
  therefore cached under `typeof(TRepository).FullName`
- **When** `Destroy<TRepository>()` is called with no explicit key, resolving to
  `typeof(TRepository).FullName`
- **Then** the entry is found, removed and destroyed

#### Scenario: Repositories sharing the destroyed store are evicted with it

- **Given** two repositories cached by the locator around the same store instance (under different keys
  or types)
- **When** `Destroy<TRepository>(key)` destroys one of them
- **Then** the other is removed from the cache too, so the next `GetRepository` call builds a fresh
  repository instead of returning one whose store was destroyed; only the targeted repository's
  `Destroy()` is invoked

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
overloads taking a pre-built store instance or a `Func<IServiceProvider, IBaseStore>` factory. The
pre-built-store overload SHALL accept only `ServiceLifetime.Singleton`, which is also its default, and SHALL
throw `ArgumentException` (paramName `lifetime`) for any other lifetime. (TASK-646: its default stayed `Scoped`
after it began refusing `Scoped`, so a call without a lifetime always threw.)

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

#### Scenario: A shared store instance can only be registered as a singleton

- **Given** `services.AddRepository<MyRepository>(storeInstance, ServiceLifetime.Transient)` (or `Scoped`)
- **When** the call runs
- **Then** `ArgumentException` is thrown naming the lifetime and pointing at
  `AddRepository<TStore, TRepository>` for a per-scope store, and nothing is registered; with
  `ServiceLifetime.Singleton`, or with the lifetime omitted, the repository is registered as a singleton around the
  captured `storeInstance`

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
`protected abstract void MapToModel(TViewModel source, TModel target)`, SHALL build a Model for a
**create** through `LoadModelInstance` (create a fresh model, then `MapToModel`), SHALL build a Model for
an **update** through `LoadModelInstanceForUpdate` / `LoadModelInstanceForUpdateAsync` (read the stored
row by `Guid`, then `MapToModel` onto it, falling back to the fresh model when no such row exists), and
SHALL build a ViewModel from a Model through `LoadInstance` (create a fresh ViewModel,
`result.LoadFrom(model)`, then `StoreHash(model)`), returning `default` when the supplied model is
`null`. Because an update maps onto a populated target, `MapToModel` SHALL assign the fields it owns
rather than accumulate into them.

#### Scenario: Reading maps model to viewmodel and seeds change tracking

- **Given** a store that returns a non-null `TModel`
- **When** `Read(filter)` runs
- **Then** a new `TViewModel` is created, `LoadFrom(model)` is invoked on it, the model's hash is
  recorded, and the ViewModel is returned

#### Scenario: A null model yields a null viewmodel

- **Given** `Store.Read(...)` returns `null`
- **When** `LoadInstance(null)` runs
- **Then** `default` is returned and no hash is stored

#### Scenario: Creating maps viewmodel to a fresh model

- **Given** a `TViewModel` carrying new values
- **When** `Create(data)` runs
- **Then** `LoadModelInstance(data)` creates a new `TModel` via `CreateModelInstance()` and populates it
  through `MapToModel`, so the model handed to the store is never the caller's instance

#### Scenario: Updating maps viewmodel onto the stored row, preserving unmapped columns

- **Given** a stored row carrying columns the ViewModel does not map (audit timestamps, a
  wrapper-injected tenant key) and a `TViewModel` carrying edited values
- **When** `Update(data)` runs on any of the four update paths — single or bulk, sync or async
- **Then** the stored row is read by `Guid`, a **detached copy** of it is taken, and `MapToModel` is
  applied to that copy, so the persisted model carries the edited values **and** the columns the
  ViewModel never mapped

#### Scenario: The merge never mutates the store's own instance

- **Given** a backend whose read returns the instance it holds (the in-memory, JSON and XML stores, and
  any caching decorator)
- **When** `Update(data)` runs and the subsequent store write fails
- **Then** the stored row is unchanged, because the ViewModel was mapped onto a detached copy rather than
  onto the instance the store handed back

#### Scenario: Updating a viewmodel whose row does not exist falls back to the fresh model

- **Given** a `TViewModel` whose `Guid` matches no stored row
- **When** `Update(data)` runs
- **Then** the freshly mapped model is handed to the store unchanged, so the store reports the missing
  row exactly as it did before, rather than the repository raising a different failure

#### Scenario: The viewmodel is refreshed from the persisted model

- **Given** a successful `Create(data)` or `Update(data)`
- **When** the store call returns
- **Then** `data.LoadFrom(item)` is executed, propagating store-assigned values (e.g. `Guid`) back into
  the caller's ViewModel

#### Scenario: Delete of a null viewmodel is a no-op

- **Given** `Delete(null)` / `DeleteAsync(null)` on an `AbstractViewModelRepository` /
  `AbstractAsyncViewModelRepository` with a non-null store and `ReadMode == false`
- **When** the call runs
- **Then** it returns early without reaching `MapToModel` or the store, as `Create` and `Update` do on
  `data == null`; a non-null ViewModel is mapped through `LoadModelInstance` on both the sync and async
  paths, so an override of that hook applies to deletes too

### Requirement: ViewModel repositories track a SHA-256 model hash that does not gate the write

The system SHALL maintain `IDictionary<Guid, byte[]> _modelHash` keyed by the model's `Guid`, backed by
a `ConcurrentDictionary<Guid, byte[]>` (with the `ReadMode` flag `volatile`) because one repository
instance is shared by every caller of a singleton registration or the locator, SHALL populate it from
`StoreHash` (only when `ReadMode` is false and `data.Guid.HasValue`) using
`StringHelper.CalculateSHA256Hash(Serializer.Serialize(data))`, SHALL refresh the stored hash on a
successful `Update`, and SHALL drop a model's hash through `RemoveHash` after a successful delete. The
hash SHALL NOT decide whether the write happens: a single-item `Update` always reaches the store.

No store can take that decision — `StoreDataDelegate<T>`'s return value is read by no backend, so the
`null!` that once expressed "skip this write" suppressed nothing. Taking the decision in the repository
instead is a behavioural change rather than a repair, because `AuditStoreWrapper`,
`TimestampStoreWrapper` and `EventSourcingStoreWrapper` all sit inside `Store.Update` in the recommended
decorator chain, so a suppressed write would also drop the audit stamp, the `UpdatedAt` bump and the
domain event. Whether to make that change is deferred.

#### Scenario: An unchanged entity is written anyway

- **Given** an entity read through the repository (hash recorded) and re-submitted unmodified
- **When** `Update(data)` runs
- **Then** the store write is issued regardless, and the decorators inside it run as they always have

#### Scenario: A changed entity is written

- **Given** an entity read through the repository and then mutated
- **When** `Update(data)` runs
- **Then** the model is handed to the store to persist and the recorded hash is refreshed afterwards

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

#### Scenario: A deleted model is no longer tracked

- **Given** a model whose hash was recorded by a read
- **When** it is deleted through the single-item `Delete(vm)` / `DeleteAsync(vm)` or the bulk
  `Delete(IEnumerable<TViewModel>)` / `DeleteAsync(IEnumerable<TViewModel>)`, and the store call returns
- **Then** `RemoveHash` drops its entry from `_modelHash` (the sync bulk delete did not until TASK-646)

#### Scenario: Concurrent reads share the hash table safely

- **Given** one ViewModel repository instance used by several threads at once
- **When** their reads each call `StoreHash`
- **Then** the writes go to a `ConcurrentDictionary`, so concurrent recording cannot corrupt the table

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
- **Then** the `Repository is in Read Mode` exception is thrown rather than the no-store exception

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

### Requirement: ViewModel repositories refuse to operate without a store

The system SHALL throw, from `AbstractViewModelRepository` and `AbstractAsyncViewModelRepository`, the
`InvalidOperationException` built by their protected `NoStore()` helper (message
`{GetType().Name} has no store; construct it with one.`) whenever `Store` is `null` on `Read`,
`ReadOne` (both forms), `Count`, `Create`, `Update` and `Delete` and their async counterparts — checked
after the read-mode guard and before the `data == null` early return — and SHALL keep `Destroy` /
`DestroyAsync` a silent no-op and `CreateModelInstance()` on its `Activator` fallback.

#### Scenario: A store-less ViewModel repository refuses a create

- **Given** an `AbstractViewModelRepository` whose `Store` is `null` and `ReadMode == false`
- **When** `Create(vm)` is called
- **Then** `InvalidOperationException` containing `has no store` is thrown rather than `Guid.Empty`
  being returned

#### Scenario: A store-less ViewModel repository refuses even with null data

- **Given** an `AbstractAsyncViewModelRepository` whose `Store` is `null`
- **When** `await UpdateAsync(null!)` is awaited
- **Then** the no-store exception is thrown, because the store check precedes the `data == null` return

#### Scenario: Destroying a store-less ViewModel repository is a no-op

- **Given** a ViewModel repository whose `Store` is `null`
- **When** `Destroy()` / `await DestroyAsync()` is called
- **Then** it returns without effect

### Requirement: ProcessDataDelegate is a transform whose result is honoured on every path

The system SHALL define `ProcessDataDelegate<TModel>` as `TModel (TModel data)` and SHALL apply it as
`item = processDelegate?.Invoke(item) ?? item` **before** the model is handed to the store, on both the
bulk and the single-item paths. It SHALL NOT be applied inside the `StoreDataDelegate<TModel>` given to
`Store.Create`/`Store.Update`, because no backend reads that delegate's return value, so a replacement
instance produced there would be discarded and the pre-transform model persisted.

#### Scenario: A wrapping delegate result is adopted on the bulk path

- **Given** a `ProcessDataDelegate<TModel>` that returns a different model instance than it is given
- **When** `Create(IEnumerable<TViewModel> data, processDelegate)` runs
- **Then** the returned instance is the one enumerated into `IBulkStore<TModel>.Create`, not the
  original projection

#### Scenario: A delegate returning null falls back to the original

- **Given** a `ProcessDataDelegate<TModel>` returning `null`
- **When** `item = processDelegate?.Invoke(item) ?? item` executes
- **Then** the pre-delegate `item` is used

#### Scenario: The single-item create path stores the hash inside the store delegate

- **Given** a single-item `Create(vm, processDelegate)`
- **When** the store invokes the repository's `StoreDataDelegate`
- **Then** `StoreHash(x)` is called on the model the store is persisting — the delegate is retained for
  this alone, because `CreateCore` assigns `data.Guid` before invoking it and the hash is keyed by `Guid`

#### Scenario: A single-item replacement instance reaches storage

- **Given** a `ProcessDataDelegate<TModel>` returning a different model instance, passed to the
  single-item `Create(vm, processDelegate)` or `Update(vm, processDelegate)`
- **When** the call runs
- **Then** the replacement instance is what the store persists, because the transform is applied before
  the store is called rather than inside a delegate whose return value is discarded

#### Scenario: A create transform can still read the key

- **Given** a `ProcessDataDelegate<TModel>` passed to a single-item `Create`
- **When** it runs
- **Then** the model already carries its `Guid`, because the repository assigns it before invoking the
  transform — preserving what the delegate had when it ran inside the store's own callback, where
  `CreateCore` assigns the key first

#### Scenario: An empty key is replaced before the transform runs

- **Given** a single-item `Create(vm, processDelegate)` / `CreateAsync(vm, processDelegate, ct)` whose
  `MapToModel` leaves the model's `Guid` as `null` or `Guid.Empty`
- **When** the call runs
- **Then** `item.EnsureGuid()` assigns a new `Guid` before the transform and the store call, so
  `Guid.Empty` is never persisted as a key; a non-empty `Guid` assigned by `MapToModel` is kept unchanged.
  The bulk `Create(IEnumerable<TViewModel>, processDelegate)` path does not call `EnsureGuid`, so a bulk
  transform runs before any key is assigned

#### Scenario: The async bulk path takes a store delegate instead

- **Given** `IAsyncBulkViewModelCreateRepository<TViewModel, TModel>.CreateAsync`
- **When** its signature is inspected
- **Then** it accepts `Stores.StoreDataDelegate<TModel>?` (forwarded straight to the store) rather than
  `ProcessDataDelegate<TModel>?`, diverging from its sync sibling

### Requirement: Bulk ViewModel repositories require a bulk store, throwing ArgumentException when called

The system SHALL check `Store is not IBulkStore<TModel>` in every `AbstractBulkViewModelRepository`
method and throw `ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}")`; SHALL
perform that check in `Read(filter, limit, offset)` itself, which validates and then returns a separate
`yield return` iterator, so the exception surfaces when `Read` is called; and SHALL return early, after
the read-mode guard and before the store check, from the collection `Create`, `Update` and `Delete` when
`data` is `null`.

#### Scenario: Bulk create against a non-bulk store throws ArgumentException

- **Given** an `AbstractBulkViewModelRepository` whose `Store` is a plain `IStore<TModel>`
- **When** `Create(items)` is called with `ReadMode == false`
- **Then** `ArgumentException` is thrown (not the `InvalidOperationException` used by the non-ViewModel
  `AbstractBulkRepository` for the same condition)

#### Scenario: The read path validates when called

- **Given** the same repository
- **When** `var seq = Read(filter, 10, 0);` executes
- **Then** the `ArgumentException` is raised by that call, before any enumeration of `seq`

#### Scenario: A null store also fails the check

- **Given** `Store == null`
- **When** any bulk ViewModel method runs with non-null `data`
- **Then** `null is not IBulkStore<TModel>` holds and `ArgumentException` is thrown

#### Scenario: Null data on a sync bulk write is silently ignored

- **Given** `ReadMode == false`
- **When** `Create((IEnumerable<TViewModel>)null!)`, `Update(null!)` or `Delete(null!)` is called
- **Then** the method returns without checking or calling the store, as the async twins do

#### Scenario: Bulk read projects each model and drops nulls

- **Given** a bulk store returning three models
- **When** `Read(filter, limit, offset)` is enumerated
- **Then** the bulk store's `Read(filter?.Filter(), orderBy, limit, offset)` is called — with `orderBy`
  being `OrderBy<TModel>.By(x => x.Guid)` when `limit` or `offset` is supplied (the ViewModel bulk
  contract takes no ordering, so a page is ordered by `Guid` to keep its window stable) and `null`
  otherwise — and each non-null `LoadInstance(item)` is yielded

### Requirement: Async bulk ViewModel repository streams results and throws without a bulk store

The system SHALL expose the async bulk read as
`IAsyncEnumerable<TViewModel> ReadAsync(Expression<Func<TModel, bool>>? filter, int? limit, int? offset, CancellationToken ct)`
with `[EnumeratorCancellation]`, SHALL throw `InvalidOperationException` when `BulkStore` is `null` —
the `NoStore()` exception when `Store` is `null`, otherwise `Store is not type of …IAsyncBulkStore…` —
SHALL pass `OrderBy<TModel>.By(x => x.Guid)` as the ordering when `limit` or `offset` is supplied (and
`null` otherwise), SHALL await the whole underlying `BulkStore.ReadAsync(...)` result before yielding,
and SHALL throw the same exception from every async bulk write when `BulkStore` is `null`, returning
early (no exception) only when `data` is `null` on a valid bulk store.

#### Scenario: Missing bulk store faults the stream

- **Given** an `AbstractAsyncBulkViewModelRepository` whose `Store` does not implement
  `IAsyncBulkStore<TModel>`
- **When** the `IAsyncEnumerable<TViewModel>` from `ReadAsync(...)` is enumerated
- **Then** the first `MoveNextAsync()` throws `InvalidOperationException` (the method is an async
  iterator, so calling `ReadAsync` itself raises nothing)

#### Scenario: Missing bulk store refuses a bulk write

- **Given** the same repository with `ReadMode == false`
- **When** `await UpdateAsync(filter, updates)` or `await DeleteAsync(filter)` is awaited
- **Then** `InvalidOperationException` is thrown and nothing is written

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

#### Scenario: Created models are projected lazily

- **Given** `CreateAsync(IEnumerable<TViewModel> data, storeDelegate, ct)`
- **When** it runs
- **Then** `data.Select(LoadModelInstance)` is passed to `BulkStore.CreateAsync` unmaterialised — no
  intermediate `ToList()` — and the store drives the enumeration

#### Scenario: Deleted models are materialised so their hashes can be dropped

- **Given** `DeleteAsync(IEnumerable<TViewModel> data, ct)`
- **When** it runs
- **Then** `data.Select(LoadModelInstance).ToList()` is passed to `BulkStore.DeleteAsync`, and after it
  returns `RemoveHash` runs for each of those models

#### Scenario: The bulk update materialises the merged models before writing

- **Given** `UpdateAsync(IEnumerable<TViewModel> data, storeDelegate, ct)`
- **When** it runs
- **Then** each ViewModel is awaited through `LoadModelInstanceForUpdateAsync` (one stored-row read per
  entity) into a `List<TModel>`, and only after every read has completed is the list passed to
  `BulkStore.UpdateAsync(items, storeDelegate, ct)`

### Requirement: SQL ViewModel repositories validate the store type through wrappers and expose the connector

The system SHALL, in `DataBaseRepository<TConnector, TViewModel, TModel>` and
`AsyncDataBaseRepository<TConnector, TViewModel, TModel>`, reject a non-null store that fails
`IsStoreOfType<TModel, DataBaseBulkStore<TConnector, TModel>>` /
`IsStoreOfType<TModel, AsyncDataBaseBulkStore<TConnector, TModel>>` with `ArgumentException`
(paramName `store`), SHALL accept wrappers around such a store, and SHALL resolve `Connector` by
unwrapping (`GetUnwrappedStore<...>()?.Connector`), yielding `null` when the innermost store is not of
the expected concrete type. Because the inherited bulk methods test the *outermost* reference, the sync
`DataBaseRepository` constructor SHALL first reject a non-null store that is not itself an
`IBulkStore<TModel>` with `ArgumentException` (paramName `store`, message beginning
`Store must be a bulk store;`); the async constructor takes an `IAsyncBulkStore<TModel>?`, so the type
system enforces the same.

#### Scenario: A non-bulk tenant wrapper is rejected by the constructor

- **Given** a `DataBaseBulkStore<SqLiteConnector, TModel>` inside a `TenantStoreWrapper`
- **When** it is passed to the `DataBaseRepository` constructor
- **Then** `ArgumentException` is thrown whose message begins `Store must be a bulk store;` and points
  at the bulk wrapper — `TenantStoreWrapper<TStore, T>` implements only `IStore<T>`/`IStoreWrapper<T>`,
  so `IsStoreOfType` alone would have accepted a repository on which every bulk method then threw; the
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
  `null`; the single-item operations throw the no-store `InvalidOperationException` and the bulk ones
  throw (`ArgumentException` on the sync repository, `InvalidOperationException` on the async one)

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

### Requirement: Per-backend repositories validate the store type and substitute a default backend store

The system SHALL provide model and ViewModel repositories per backend — `MongoDBModelRepository<T>` /
`MongoDBRepository<TViewModel, TModel>`, `RavenDBModelRepository<T>` / `RavenDBRepository<TViewModel, TModel>`,
`CosmosDBModelRepository<T>`, `InfluxDBModelRepository<T>` / `InfluxDBRepository<TViewModel, TModel>`, each with
an `Async…` sibling, plus the SQL model repositories `DataBaseModelRepository<TConnector, T>` and
`AsyncDataBaseModelRepository<T>` — deriving from the matching abstract bulk repository. Each store-taking
constructor SHALL reject a non-null store that fails `IsStoreOfType<T, XStore<T>>` with
`ArgumentException` (paramName `store`, message `Store must be of type … or a wrapper around it…`), and SHALL
reach the backend through a property resolved by `GetUnwrappedStore` (`MongoDBStore`, `RavenDBStore`,
`CosmosStore`, `InfluxDBStore`, `Connector` / `DataBaseStore`). The MongoDB, RavenDB, CosmosDB and InfluxDB
repositories SHALL replace a `null` store (and the parameterless constructor) with a new default backend store
(`Store = store ?? new XStore<T>()`), whereas the SQL model repositories SHALL leave `Store` `null` when given
`null`.

#### Scenario: A null store is replaced by a default backend store

- **Given** `new MongoDBModelRepository<T>(store: null)` (or the RavenDB, CosmosDB or InfluxDB equivalent,
  sync or async)
- **When** construction completes
- **Then** `Store` is a newly constructed backend store, so the no-store refusal of the abstract
  repositories never engages for these types

#### Scenario: An unrelated store is rejected by a backend repository

- **Given** an `InMemoryStore<T>`
- **When** it is passed to `RavenDBModelRepository<T>(IStore<T>?)`
- **Then** `ArgumentException` is thrown with message
  `Store must be of type RavenDBStore<T> or a wrapper around it.`

#### Scenario: Settings and health go to the unwrapped backend store

- **Given** a backend repository over a wrapped backend store
- **When** `SetSettings(settings)` or `IsHealthy()` is called
- **Then** the call reaches the unwrapped store; `SetSettings(null)` and a store that does not unwrap to the
  backend type are ignored, and `IsHealthy()` then returns `false`

#### Scenario: RavenDB and CosmosDB repositories build their own store from connection details

- **Given** `new RavenDBModelRepository<T>(connectionString, databaseName)` /
  `new RavenDBModelRepository<T>(documentStore)`, or
  `new CosmosDBModelRepository<T>(connectionString, databaseName, containerName)` /
  `new CosmosDBModelRepository<T>(container)` (and the async and ViewModel siblings where declared)
- **When** construction completes
- **Then** `Store` is a new backend store built from those arguments, with no type validation

#### Scenario: The SQL model repository exposes the connector and init hooks

- **Given** a `DataBaseModelRepository<TConnector, T>` (which implements
  `IDataBaseRepository<TConnector, T> : IRepository<T>`)
- **When** `Connector`, `AddOnInit(hook)` or `RemoveOnInit(hook)` is used
- **Then** the unwrapped `DataBaseBulkStore<TConnector, T>` supplies the connector and receives the hook; a
  `null` hook or a missing store is ignored; the parameterless constructor chains to
  `this(new DataBaseBulkStore<TConnector, T>())`

#### Scenario: The async SQL model repository is fixed to the abstract connector type

- **Given** `AsyncDataBaseModelRepository<T>`, whose type check is
  `IsStoreOfType<T, AsyncDataBaseBulkStore<AbstractConnector, T>>`
- **When** it is constructed with a store typed `AsyncDataBaseBulkStore<SqLiteConnector, T>` (or any other
  concrete connector)
- **Then** generic invariance makes the check fail and `ArgumentException` is thrown, so only a store
  closed over `AbstractConnector` itself is accepted; the class has no parameterless constructor and no
  `TConnector` parameter

### Requirement: An ordered single read goes through the decorated store, never through the connector

The system SHALL expose the ordered single read as
`AbstractViewModelRepository.ReadOne(IFilter<TModel>?, OrderBy<TModel>?)`, reading through `Store` — the
**decorated** chain — and SHALL NOT provide any repository-level read that reaches
`repository.Connector`.

`Connector` resolves as `Store?.GetUnwrappedStore<…>()?.Connector`, and `GetUnwrappedStore` walks
`IStoreWrapper.GetInnerStore()` to the **innermost** store, so a read issued through it applies no
decorator at all. `TenantStoreWrapper.Read` is what injects the `ModelByTenant` predicate; without it the
read returns the first matching row from **any** tenant, and the soft-delete, localization and audit
wrappers are skipped by the same call (SH-H036).

The ordered read SHALL therefore be an **instance** method rather than an extension: `Store` is
`protected`, so an extension in another assembly cannot reach the decorated chain and `Connector` is the
only handle available to it — the capability is not implementable safely from outside the class.

Where the configured store does not implement `IBulkReadStore<T>` (as `TenantStoreWrapper`, which
implements only `IStore<T>`/`IStoreWrapper<T>`, does not), the ordering SHALL be dropped and the read
SHALL degrade to the unordered `Read(filter)`. Correct-but-unordered is the required trade; the
alternative that preserved ordering is precisely the connector bypass.

#### Scenario: An ordered read under a tenant does not return another tenant's row

- **Given** a repository over a `TenantStoreWrapper` scoped to tenant *t*, and a row belonging to tenant
  *u* that matches the filter
- **When** `ReadOne(filter, orderBy)` is called
- **Then** no row is returned — and the caller's own matching row is still returned, so the scoping is a
  narrowing rather than a blanket refusal

#### Scenario: Adding an ordering does not change a read's isolation

- **Given** the same repository and filter
- **When** `ReadOne(filter)` and `ReadOne(filter, orderBy)` are compared
- **Then** both are tenant-scoped. Previously these were two different methods — a safe instance method
  and an unsafe same-named extension differing only in arity, with C# preferring the instance method — so
  adding an ordering to a working call silently dropped tenant scoping

#### Scenario: Ordering is honoured when the store supports bulk reads

- **Given** a repository over an undecorated `IBulkReadStore<T>`
- **When** `ReadOne(null, OrderBy<T>.By(...))` and its descending counterpart are called
- **Then** the first and last rows in that ordering are returned respectively

#### Scenario: Ordering degrades rather than bypassing when the decorator is not a bulk store

- **Given** a repository over a `TenantStoreWrapper`, which implements `IStore<T>` but not
  `IBulkReadStore<T>`
- **When** `ReadOne(filter, orderBy)` is called
- **Then** a correctly tenant-scoped row is returned with the ordering not applied — the decorator is
  never unwrapped to honour it

#### Scenario: No store is refused

- **Given** a repository whose `Store` is `null`
- **When** `ReadOne` is called in either form
- **Then** the no-store `InvalidOperationException` is thrown without querying anything

#### Scenario: Unwrapping a decorated store is the escape hatch, and it strips tenant scoping

- **Given** a `TenantStoreWrapper` over a store holding another tenant's row
- **When** the same query is issued through the wrapper and through `GetUnwrappedStore()`
- **Then** the wrapper returns nothing and the unwrapped store returns the foreign row. This is
  **intended** behaviour of the escape hatch — the `XStore` / `Connector` properties exist so callers can
  reach backend-native features a portable store cannot express — but it is why those properties must
  never serve a portable read

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
