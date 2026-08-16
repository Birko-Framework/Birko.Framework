---
area: store-crud-contract
generated-at: 5b4c2b4ef9fa19a2e1d6ed48378861579a3bf5a4
generated-on: 2026-08-06
sources:
  - ../Birko.Data.Core/Exceptions/StoreException.cs
  - ../Birko.Data.InMemory/Stores/AbstractAsyncInMemoryStore.cs
  - ../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs
  - ../Birko.Data.InMemory/Stores/AsyncInMemoryStore.cs
  - ../Birko.Data.InMemory/Stores/InMemoryStore.cs
  - ../Birko.Data.Stores/AbstractAsyncStore.cs
  - ../Birko.Data.Stores/AbstractStore.cs
  - ../Birko.Data.Stores/IAsyncStore.cs
  - ../Birko.Data.Stores/IStore.cs
  - ../Birko.Data.Stores/IStoreWrapper.cs
  - ../Birko.Data.Stores/StoreExtensions.cs
  - ../Birko.Data.Stores/StoreLocator.cs
source-commits:   # sibling baselines. RECONSTRUCTED 2026-08-16 from generated-on, not
                  # recorded at regen time -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Data.Core: 11da2ac
  ../Birko.Data.InMemory: 4f680b7
  ../Birko.Data.Stores: 3cd8b2a
shaped-by: [FEATURE-014]
# false, and NOT because nobody tried — see the identical note in bulk-filter-operations.md: every
# source glob points into a sibling repo, so no task's `pr:` sha resolves under `git show` in this
# aggregator. FEATURE-014 comes from the regenerating task's `feature:` field, not from evidence.
shaped-by-derived: false
---

# Store CRUD contract and template-method hierarchy

## Purpose

Every persistence backend in the framework — SQL, MongoDB, ElasticSearch, JSON, XML, Redis, in-memory —
exposes the same small set of CRUD operations so that callers (repositories, decorator wrappers, tenant
scoping, view stores) can be written once against an abstraction instead of once per database. This
capability defines that abstraction: the segregated operation interfaces (`ICreateStore<T>`,
`IReadStore<T>`, `IUpdateStore<T>`, `IDeleteStore<T>`, `ICountStore<T>`, `IBaseStore`) and their async
twins; the abstract base classes `AbstractStore<T>` / `AbstractAsyncStore<T>` that implement the public
surface as a template method (public wrapper opens a lazy-initialization gate, then delegates to a
`protected abstract *Core` member a concrete store overrides); the process-wide `StoreLocator` cache that
hands out store singletons keyed by settings identity; the `IStoreWrapper` unwrapping helpers that let a
caller reach past decorator layers to the real backend; and `Birko.Data.InMemory`, the simplest concrete
implementation of the whole contract — a thread-safe dictionary with no persistence, used as the canonical
test double and as the reference for what a `*Core` override is obliged to do.

## Requirements

### Requirement: Segregated operation interfaces compose into a complete store interface

The system SHALL define each store operation as its own single-method interface and SHALL compose them
into `IStore<T>` (sync) and `IAsyncStore<T>` (async), both constrained to `T : Models.AbstractModel`.

`IStore<T>` composes `IBaseStore` (`Init`, `Destroy`), `ICountStore<T>` (`long Count(filter)`),
`IReadStore<T>` (`T? Read(Guid)`, `T? Read(filter)`), `ICreateStore<T>` (`Guid Create(data, storeDelegate)`),
`IUpdateStore<T>` (`void Update(data, storeDelegate)`) and `IDeleteStore<T>` (`void Delete(data)`), and adds
`T CreateInstance()` and `Guid Save(data, storeDelegate)`.

`IAsyncStore<T>` composes `IAsyncBaseStore` (`InitAsync`, `DestroyAsync`), `IAsyncCountStore<T>`,
`IAsyncReadStore<T>`, `IAsyncCreateStore<T>`, `IAsyncUpdateStore<T>` and `IAsyncDeleteStore<T>`, and adds
`Task<Guid> SaveAsync(...)` and `T CreateInstance()`. Every async member accepts a trailing
`CancellationToken ct = default`.

#### Scenario: Caller depends only on the read half of the contract

- **Given** a component that only needs to look entities up
- **When** it declares its dependency as `IReadStore<Invoice>` rather than `IStore<Invoice>`
- **Then** it compiles against just `Read(Guid)` and `Read(Expression<Func<Invoice,bool>>?)`, because the
  read operations are declared in their own interface

#### Scenario: Update reports no identifier

- **Given** the `IUpdateStore<T>` contract
- **When** `Update(data, storeDelegate)` is called
- **Then** it returns `void` — unlike `Create`, which returns the `Guid` of the created entity — so a caller
  wanting the identifier after an update must read it from the entity it passed in

#### Scenario: Settings-aware store combines both contracts, sync only

- **Given** `ISettingsStore<T, TSettings> : IStore<T>, ISettingsStore<TSettings> where TSettings : ISettings`
- **When** a caller looks for the equivalent combined interface for asynchronous stores
- **Then** none exists — `IAsyncStore.cs` declares no async counterpart of `ISettingsStore<T, TSettings>`, so
  an async store must implement `IAsyncStore<T>` and the non-generic `ISettingsStore<TSettings>` separately

### Requirement: Public CRUD members open a lazy-initialization gate before delegating to a Core override

The system SHALL implement each public CRUD member of `AbstractStore<T>` / `AbstractAsyncStore<T>` as a
`virtual` wrapper that first calls `EnsureInitialized()` / `EnsureInitializedAsync(ct)` and then delegates to
a `protected abstract` `*Core` member. `Create`→`CreateCore`, `Read(filter)`→`ReadCore(filter)`,
`Update`→`UpdateCore`, `Delete`→`DeleteCore`, `Count`→`CountCore`, and the async equivalents
`CreateCoreAsync` / `ReadCoreAsync` / `UpdateCoreAsync` / `DeleteCoreAsync` / `CountCoreAsync`. Concrete
stores therefore never have to repeat the initialization call.

#### Scenario: First operation on a never-initialized store initializes it

- **Given** a freshly constructed store on which neither `Init()` nor any CRUD method has been called
- **When** the caller invokes `Count()`
- **Then** `EnsureInitialized()` runs `InitCore()` exactly once, sets the `_initialized` flag, and only then
  is `CountCore(null)` invoked

#### Scenario: Initialization runs at most once under concurrency

- **Given** several threads calling CRUD methods on the same uninitialized `AbstractStore<T>`
- **When** they race
- **Then** double-checked locking over a private `object _initLock` (a `SemaphoreSlim(1,1)` in the async base)
  guarantees `InitCore()` / `InitCoreAsync(ct)` executes once; `_initialized` is a plain non-volatile `bool`
  read outside the lock on the fast path

#### Scenario: Failed initialization is retried on the next call

- **Given** an `InitCore()` implementation that throws (for example, the database is unreachable)
- **When** the first `Create(data)` fails and the caller retries
- **Then** `_initialized` was never set to `true` (it is assigned only after `InitCore()` returns), so the
  retry attempts initialization again rather than proceeding against an uninitialized store

### Requirement: Init and InitAsync are idempotent aliases of the initialization gate

The system SHALL implement `Init()` as a call to `EnsureInitialized()` and `InitAsync(ct)` as a call to
`EnsureInitializedAsync(ct)`, so explicit initialization is optional and repeatable.

#### Scenario: Init called twice

- **Given** a store whose `InitCore()` creates a table
- **When** the caller invokes `Init()` and then `Init()` again
- **Then** `InitCore()` runs only on the first call; the second returns immediately on the `_initialized`
  fast path

#### Scenario: Init after CRUD has already implicitly initialized

- **Given** a store on which `Create(data)` has already run and thereby initialized the backend
- **When** the caller then invokes `Init()` defensively
- **Then** nothing happens — no second `InitCore()`

### Requirement: The async initialization gate observes cancellation on every operation

The system SHALL call `ct.ThrowIfCancellationRequested()` at the top of `EnsureInitializedAsync`, before the
`_initialized` fast-path check, so that a cancelled token aborts an operation even on an already-initialized
store. The synchronous gate has no cancellation equivalent, because no synchronous member accepts a token.

#### Scenario: Cancelled token on an already-initialized async store

- **Given** an `AbstractAsyncStore<T>` that has completed initialization
- **When** `CountAsync(null, cancelledToken)` is awaited
- **Then** `EnsureInitializedAsync` throws `OperationCanceledException` and `CountCoreAsync` is never reached,
  even though no work would have been needed to initialize

#### Scenario: Token cancelled while waiting for the initialization lock

- **Given** one caller inside `InitCoreAsync` holding the `SemaphoreSlim`
- **When** a second caller awaits `_initLock.WaitAsync(ct)` and its token is cancelled
- **Then** the wait throws, the `try` block is never entered, and the semaphore is not released by the second
  caller because `Release()` sits in the `finally` of that same `try`

### Requirement: Read by identifier is expressed as a filter in the abstract bases

The system SHALL implement `AbstractStore<T>.Read(Guid guid)` and `AbstractAsyncStore<T>.ReadAsync(Guid guid,
ct)` by constructing `Filters.ModelByGuid<T>(guid).Filter()` — the predicate `x => x.Guid == guid` — and
delegating to the filter-taking overload, so that both paths share one matching rule. Neither method calls the
initialization gate itself; the gate is opened by the filter overload it delegates to.

#### Scenario: Backend with no identifier fast path

- **Given** a concrete store that overrides only `ReadCore(filter)` and not `Read(Guid)`
- **When** `Read(someGuid)` is called
- **Then** the store receives a compiled/translated `x => x.Guid == someGuid` predicate through `ReadCore`,
  and no separate by-identifier code path exists

#### Scenario: Reading the default identifier through the base implementation

- **Given** `AbstractModel.Guid` is `Guid?` and defaults to `null`
- **When** the base `Read(Guid.Empty)` is called
- **Then** the predicate is `x => x.Guid == Guid.Empty`, which matches entities whose `Guid` is explicitly
  `Guid.Empty` and does not match entities whose `Guid` is `null`

### Requirement: Save routes between create and update by identifier presence and never throws on null

The system SHALL implement `Save` / `SaveAsync` to return `Guid.Empty` when `data` is `null`; to call
`Create` / `CreateAsync` when `data.Guid` is `null` or `Guid.Empty`, returning the identifier that create
reported; and otherwise to call `Update` / `UpdateAsync` and return `data.Guid!.Value`.

#### Scenario: Saving a new entity

- **Given** an entity whose `Guid` is `null`
- **When** `Save(entity)` is called
- **Then** `Create(entity, storeDelegate)` runs and its returned `Guid` is the result of `Save`

#### Scenario: Saving null

- **Given** `data` is `null`
- **When** `Save(null)` is called
- **Then** `Guid.Empty` is returned; no `ArgumentNullException` is thrown and the initialization gate is
  never opened, because `Save` returns before reaching `Create` or `Update`

#### Scenario: Saving an entity whose identifier is not in the store

- **Given** an entity carrying a non-empty `Guid` that no stored record uses
- **When** `Save(entity)` is called against an in-memory store
- **Then** `Update` is chosen, `UpdateCore` finds no matching key and does nothing, and `Save` still returns
  `entity.Guid.Value` — so the caller receives a plausible identifier for data that was never persisted

### Requirement: CreateInstance constructs the entity type reflectively with a fallback attempt

The system SHALL implement `CreateInstance()` in both abstract bases as `Activator.CreateInstance<T>()`,
catching `MissingMethodException` and then attempting
`(T)Activator.CreateInstance(typeof(T), Array.Empty<object>())!`.

#### Scenario: Entity type has a public parameterless constructor

- **Given** `T` is a model with a public parameterless constructor
- **When** `CreateInstance()` is called
- **Then** a new `T` is returned by the first `Activator.CreateInstance<T>()` call

#### Scenario: Entity type has no public parameterless constructor

- **Given** `T` exposes only a constructor taking arguments
- **When** `CreateInstance()` is called
- **Then** the first call throws `MissingMethodException`, the catch block issues an equivalent
  zero-argument `Activator.CreateInstance` call, and that call fails the same way — so
  `MissingMethodException` propagates to the caller and the fallback changes nothing

### Requirement: Destroy tears down stored data without resetting the initialization latch

The system SHALL declare `Destroy()` / `DestroyAsync(ct)` abstract in the bases, without opening the
initialization gate, and SHALL NOT reset the private `_initialized` flag when they run.

The declared and the implemented meaning of the operation disagree, and the spec records both: the XML
documentation on `IBaseStore.Destroy()` and `IAsyncBaseStore.DestroyAsync(ct)` reads "Destroys the store and
releases all resources", which is disposal wording, while every implementation deletes stored data — the
in-memory stores clear `_items`, and backend stores drop the underlying database, container, collection or
file. No type in this contract implements `IDisposable` or `IAsyncDisposable`, so `Destroy` is also the only
teardown member a caller can find.

#### Scenario: Caller reads Destroy as resource cleanup

- **Given** the `IBaseStore.Destroy()` documentation "Destroys the store and releases all resources" and no
  `IDisposable` / `IAsyncDisposable` implementation anywhere in the contract
- **When** a caller invokes it to release connections or handles at the end of a scope
- **Then** stored data is destroyed instead — the implemented meaning is the one that runs, and the
  documentation gives no warning of it

#### Scenario: Destroying an in-memory store

- **Given** an initialized `AbstractInMemoryStore<T>` holding entities
- **When** `Destroy()` is called
- **Then** `_items.Clear()` empties the backing dictionary and the store keeps serving CRUD calls against the
  now-empty dictionary

#### Scenario: CRUD after Destroy does not re-initialize

- **Given** any store whose `InitCore()` provisions backend structures and whose `Destroy()` removes them
- **When** a CRUD method is invoked after `Destroy()`
- **Then** `EnsureInitialized()` sees `_initialized == true` and returns immediately, so `InitCore()` is not
  re-run and the operation proceeds against torn-down storage

### Requirement: The store data delegate can only influence a write by mutating the entity in place

The system SHALL declare `StoreDataDelegate<T>(T data)` as returning `T`, and SHALL discard that return value
at every invocation site in this area — `storeDelegate?.Invoke(data)` / `processDelegate?.Invoke(data)` —
persisting the original `data` reference instead.

#### Scenario: Delegate stamps a field

- **Given** a delegate `d => { d.Guid ??= Guid.NewGuid(); return d; }`
- **When** `Create(entity, delegate)` reaches `AbstractInMemoryStore.CreateCore`
- **Then** the mutation is visible, because the same `entity` reference is the one written into `_items`

#### Scenario: Delegate returns a different instance

- **Given** a delegate that builds and returns a new, modified copy of the entity
- **When** `Create(entity, delegate)` runs
- **Then** the returned copy is thrown away and the unmodified `entity` is stored

#### Scenario: Delegate parameter name differs between sync and async

- **Given** a caller using named arguments
- **When** it writes `storeDelegate:` for `Create` and reuses the same name for `CreateAsync`
- **Then** the async call does not compile — `IAsyncCreateStore<T>.CreateAsync`, `UpdateAsync`, `SaveAsync`
  and the corresponding `*CoreAsync` members name the parameter `processDelegate`, while the sync members and
  `AbstractAsyncInMemoryStore.CreateCoreAsync(IEnumerable<T>, …)` name it `storeDelegate`

### Requirement: The in-memory store backs the entity set with a concurrent dictionary keyed by Guid

The system SHALL store entities in a `protected readonly ConcurrentDictionary<Guid, T> _items` in
`AbstractInMemoryStore<T>` and `AbstractAsyncInMemoryStore<T>`, expose it as
`public IReadOnlyDictionary<Guid, T> Items`, implement `InitCore` / `InitCoreAsync` as no-ops (the dictionary
is created in its field initializer), and lose all data when the instance is discarded.

#### Scenario: Test asserts on stored state directly

- **Given** a test using `InMemoryStore<Invoice>` as a stand-in for a SQL store
- **When** it needs to verify what was persisted
- **Then** it reads `store.Items`, a read-only view over the live dictionary

#### Scenario: No initialization work is required

- **Given** a new `AsyncInMemoryStore<Invoice>`
- **When** the initialization gate calls `InitCoreAsync(ct)`
- **Then** `Task.CompletedTask` is returned without touching the token or the dictionary

### Requirement: In-memory create assigns a missing identifier and upserts by key

The system SHALL, in `CreateCore` / `CreateCoreAsync`, return `Guid.Empty` without storing anything when
`data` is `null`; otherwise assign `data.Guid ??= Guid.NewGuid()`, invoke the delegate, write
`_items[data.Guid.Value] = data`, and return `data.Guid.Value`.

#### Scenario: Creating an entity with no identifier

- **Given** an entity whose `Guid` is `null`
- **When** `Create(entity)` is called
- **Then** a fresh `Guid` is generated, assigned onto the entity, used as the dictionary key, and returned

#### Scenario: Creating over an existing key

- **Given** `_items` already contains an entity under `g`
- **When** `Create(other)` is called with `other.Guid == g`
- **Then** the indexer assignment silently replaces the stored entity — there is no duplicate-key detection
  and no exception, so create behaves as an upsert

#### Scenario: Creating null

- **Given** `data` is `null`
- **When** `Create(null)` is called
- **Then** `Guid.Empty` is returned and `_items` is unchanged — the same return value a successful create of
  an entity carrying `Guid.Empty` would produce

#### Scenario: Creating an entity whose identifier is already Guid.Empty

- **Given** an entity whose `Guid` is `Guid.Empty` rather than `null`
- **When** `Create(entity)` is called
- **Then** `??=` does not replace it, the entity is stored under the `Guid.Empty` key, and `Guid.Empty` is
  returned

### Requirement: In-memory read by identifier is an O(1) lookup that rejects the empty Guid

The system SHALL override the public `Read(Guid)` / `ReadAsync(Guid, ct)` in the in-memory stores to open the
initialization gate and then return `_items[guid]` only when `guid != Guid.Empty` and the key exists,
otherwise `null` — bypassing the base classes' `ModelByGuid` filter path.

#### Scenario: Existing entity fetched by key

- **Given** `_items` contains an entity under `g`
- **When** `Read(g)` is called
- **Then** `TryGetValue` returns it directly, with no predicate compilation or scan

#### Scenario: Unknown identifier

- **Given** `g` is not a key in `_items`
- **When** `Read(g)` is called
- **Then** `null` is returned

#### Scenario: Empty Guid diverges from the base implementation

- **Given** an entity stored under the `Guid.Empty` key
- **When** `Read(Guid.Empty)` is called on the in-memory store
- **Then** the `guid != Guid.Empty` guard short-circuits and `null` is returned, whereas the base
  `AbstractStore<T>.Read(Guid)` — which builds `x => x.Guid == Guid.Empty` — would have found that entity;
  the entity is reachable in-memory only through `Read(filter)`

### Requirement: In-memory single-entity read returns the first match in dictionary order

The system SHALL implement `ReadCore(filter)` / `ReadCoreAsync(filter, ct)` as
`_items.Values.FirstOrDefault(x => predicate?.Invoke(x) ?? true)`, compiling the filter expression with
`filter?.Compile()`.

#### Scenario: Filter matches several entities

- **Given** three stored entities satisfying the predicate
- **When** `Read(filter)` is called
- **Then** one of them is returned — whichever `ConcurrentDictionary.Values` enumerates first, which is not a
  guaranteed or insertion order

#### Scenario: No filter supplied

- **Given** `filter` is `null`
- **When** `Read()` is called on a non-empty store
- **Then** the null-coalesced predicate is `true` for every entity and an arbitrary stored entity is
  returned rather than `null`

#### Scenario: Empty store

- **Given** `_items` is empty
- **When** `Read(filter)` is called
- **Then** `FirstOrDefault` yields `null`

### Requirement: In-memory update and delete silently ignore entities they cannot act on

The system SHALL, in `UpdateCore` / `UpdateCoreAsync`, apply the delegate and write
`_items[data.Guid.Value] = data` only when `data?.Guid != null` and `_items.ContainsKey(data.Guid.Value)`,
and otherwise do nothing; and SHALL, in `DeleteCore` / `DeleteCoreAsync`, call `_items.TryRemove` when
`data?.Guid != null`, discarding the result.

#### Scenario: Updating an entity that is not stored

- **Given** an entity with a non-empty `Guid` absent from `_items`
- **When** `Update(entity)` is called
- **Then** nothing is written and no exception is raised — the caller cannot tell the update was dropped

#### Scenario: Updating an entity with no identifier

- **Given** an entity whose `Guid` is `null`
- **When** `Update(entity)` is called
- **Then** the guard fails, the delegate is never invoked, and nothing is stored

#### Scenario: Deleting an entity twice

- **Given** an entity that has already been deleted
- **When** `Delete(entity)` is called again
- **Then** `TryRemove` returns `false`, the result is discarded, and the call completes successfully —
  delete is idempotent and silent

#### Scenario: Deleting an entity with no identifier

- **Given** `data.Guid` is `null` (or `data` itself is `null`)
- **When** `Delete(data)` is called
- **Then** the null-conditional guard skips removal entirely

### Requirement: In-memory count uses the dictionary size when unfiltered

The system SHALL return `_items.Count` from `CountCore` / `CountCoreAsync` when `filter` is `null`, and
otherwise `_items.Values.Count(filter.Compile())`.

#### Scenario: Counting everything

- **Given** five stored entities
- **When** `Count()` is called with no filter
- **Then** `5` is returned without compiling any expression or enumerating values

#### Scenario: Counting with a predicate

- **Given** two of five entities satisfy the filter
- **When** `Count(filter)` is called
- **Then** the expression is compiled and `2` is returned

### Requirement: In-memory bulk read applies filter, ordering, offset and limit in that order and returns a snapshot

The system SHALL implement the bulk `ReadCore(filter, orderBy, limit, offset)` /
`ReadCoreAsync(filter, orderBy, limit, offset, ct)` as: optional `Where` on the compiled predicate, then
`OrderByHelper.ApplyTo(result, orderBy)`, then `Skip(offset)` when supplied, then `Take(limit)` when
supplied, then `ToList()` so the result is materialized before return.

#### Scenario: Paging request

- **Given** a request with `offset = 20` and `limit = 10`
- **When** the bulk read runs
- **Then** ordering is applied first, then twenty entities are skipped and ten taken — so the page is
  computed against the ordered sequence, not the raw dictionary order

#### Scenario: Concurrent mutation during enumeration

- **Given** another thread creating entities while a caller iterates the returned sequence
- **When** the bulk read returns
- **Then** the caller holds an already-materialized `List<T>` snapshot and is unaffected by later dictionary
  mutations

#### Scenario: Limit without offset

- **Given** `offset` is `null` and `limit` is `5`
- **When** the bulk read runs
- **Then** no `Skip` is applied and the first five ordered entities are returned

### Requirement: In-memory bulk write operations skip null and unusable items instead of failing

The system SHALL treat a `null` collection as a no-op in bulk `CreateCore`, `UpdateCore` and `DeleteCore`
(and their async twins); SHALL skip `null` items in bulk create; SHALL restrict bulk update to items with
`Guid.HasValue` whose key already exists in `_items`; and SHALL restrict bulk delete to items with
`Guid.HasValue`.

#### Scenario: Bulk create over a collection containing nulls

- **Given** a collection of three entities of which one element is `null`
- **When** bulk `Create(data)` runs
- **Then** the `null` is filtered out, the other two receive generated identifiers where missing, and both
  are upserted

#### Scenario: Bulk update where only some items exist

- **Given** a collection of four entities, two of which have identifiers present in `_items`
- **When** bulk `Update(data)` runs
- **Then** only the two present entities are written; the delegate is not invoked for the other two and no
  error reports the partial application

#### Scenario: Bulk operation with a null collection

- **Given** `data` is `null`
- **When** bulk `Create(null)`, `Update(null)` or `Delete(null)` runs
- **Then** the method returns immediately (`Task.CompletedTask` in the async store) leaving `_items` untouched

### Requirement: In-memory filter-based delete requires a filter, then snapshots the matches before removing them

The system SHALL override the public `Delete(Expression<Func<T, bool>> filter)` /
`DeleteAsync(filter, ct)` in the in-memory stores to **first refuse a null filter**, then open the
initialization gate, compile the predicate, materialize the matching key/value pairs with `.ToList()`, and
`TryRemove` each key.

The null check SHALL be repeated **in the override itself** rather than inherited. Overriding the *public*
method bypasses the base class's guard entirely, so the base cannot enforce the invariant for it — which
is precisely why the family convention has concrete stores override `protected *Core` and not the public
CRUD methods (SH-M023). These overrides predate the guard and stand against that convention; repeating
the check is the contained fix, and converting them to `*Core` is separate work.

#### Scenario: Deleting all entities matching a predicate

- **Given** ten stored entities of which four match the filter
- **When** `Delete(filter)` is called
- **Then** exactly those four keys are removed in one pass, and the matches were captured into a list before
  any removal so the dictionary is not mutated during its own enumeration

#### Scenario: Filter matches nothing

- **Given** no stored entity satisfies the filter
- **When** `Delete(filter)` is called
- **Then** the snapshot list is empty, no removal occurs, and no exception is raised

#### Scenario: A null filter is refused rather than clearing the whole collection

- **Given** an in-memory store holding entities
- **When** `Delete(null!)` / `DeleteAsync(null!, ct)` is called
- **Then** the override throws `ArgumentNullException` naming the `filter` parameter, and **no** entity is
  removed — a compiled null predicate would otherwise have matched everything

#### Scenario: Every row is still reachable deliberately

- **Given** a caller that genuinely wants the collection emptied
- **When** they call `DeleteAll()` (or pass an explicit `x => true`)
- **Then** the operation proceeds — the guard removes the *accidental* whole-collection delete, not the
  deliberate one

### Requirement: In-memory stores support aggregation over the live entity set

The system SHALL implement `IAggregatableStore<T>.Aggregate(AggregateQuery<T>)` on
`AbstractInMemoryStore<T>` and `IAsyncAggregatableStore<T>.AggregateAsync(AggregateQuery<T>, ct)` on
`AbstractAsyncInMemoryStore<T>`, each opening the initialization gate and then delegating to
`AggregateHelper.LinqAggregate(_items.Values, query)` / `AggregateHelper.LinqAggregateAsync(_items.Values,
query, ct)`.

#### Scenario: Aggregating an in-memory set

- **Given** an initialized `AsyncInMemoryStore<Order>` holding orders
- **When** `AggregateAsync(query, ct)` is awaited
- **Then** the initialization gate runs first (observing `ct`) and the aggregation is evaluated in-process by
  the shared LINQ aggregation helper over `_items.Values`

### Requirement: The async in-memory store mirrors the sync store and completes synchronously

The system SHALL implement every `*CoreAsync` member of `AbstractAsyncInMemoryStore<T>` with the same body as
its synchronous counterpart, returning `Task.FromResult(...)` or `Task.CompletedTask`, and SHALL NOT check
the cancellation token inside those bodies — cancellation is observed only by the
`EnsureInitializedAsync(ct)` call the public wrapper makes.

#### Scenario: Async write on a cancelled token

- **Given** an already-initialized `AsyncInMemoryStore<T>`
- **When** `CreateAsync(entity, null, cancelledToken)` is awaited
- **Then** the operation still throws, because `EnsureInitializedAsync` checks the token unconditionally
  before `CreateCoreAsync` is reached

#### Scenario: No thread is yielded

- **Given** any async in-memory CRUD call on an initialized store
- **When** it is awaited
- **Then** the work has already completed on the calling thread and the returned task is already finished

### Requirement: In-memory stores accept settings for drop-in compatibility and never read them

The system SHALL have `InMemoryStore<T>` and `AsyncInMemoryStore<T>` implement both
`ISettingsStore<Settings>` and `ISettingsStore<ISettings>`, offer a parameterless constructor and a
`(Settings)` constructor, store the value in `protected Settings? _settings`, and never consult it for
storage. `SetSettings(ISettings)` SHALL apply the value only when the argument is the concrete
`Birko.Configuration.Settings` type (or a subclass of it) and SHALL otherwise do nothing.

#### Scenario: In-memory store substituted for a file-backed store in a test

- **Given** production code that constructs its store with a `Settings` carrying `Location` and `Name`
- **When** a test swaps in `new InMemoryStore<Invoice>(settings)`
- **Then** the construction compiles and succeeds, and the settings have no effect on where data lives

#### Scenario: A foreign ISettings implementation is dropped

- **Given** a custom type implementing `ISettings` that does not derive from `Birko.Configuration.Settings`
- **When** `SetSettings(thatInstance)` is called through the `ISettings` overload
- **Then** the `is Settings concrete` pattern fails, `_settings` is left unchanged, and no exception reports
  the discard

#### Scenario: A derived settings type is applied

- **Given** a `SqlSettings` (a descendant of `Settings`)
- **When** it is passed through `SetSettings(ISettings)`
- **Then** the pattern match succeeds and `_settings` is assigned, even though the store never reads it

### Requirement: StoreLocator caches one store instance per settings identity and store type

The system SHALL maintain a process-wide `IDictionary<string, IDictionary<Type, object>>` keyed first by
`settings?.GetId() ?? string.Empty` and then by `typeof(TStore)`; SHALL create a missing entry with
`Activator.CreateInstance(type, new object[] { })`; and SHALL return the cached instance on subsequent calls.
`GetStore<TStore>()` SHALL delegate to `GetStore<TStore, ISettings>(default!)`, which resolves to the
empty-string identity bucket.

#### Scenario: Two lookups with the same settings

- **Given** two calls to `GetStore<MyStore, SqlSettings>(settings)` with settings whose `GetId()` is `"db1"`
- **When** both complete
- **Then** the same instance is returned, constructed once

#### Scenario: Same store type under two settings identities

- **Given** `GetStore<MyStore, SqlSettings>(settingsA)` and `GetStore<MyStore, SqlSettings>(settingsB)` with
  different `GetId()` values
- **When** both are called
- **Then** two independent instances exist, one per identity bucket

#### Scenario: Parameterless lookup

- **Given** `GetStore<MyStore>()` with no settings
- **When** it runs
- **Then** `settings` is `null`, the identity is `string.Empty`, and no `SetSettings` call is attempted

#### Scenario: Store type without a public parameterless constructor

- **Given** a `TStore` whose only constructor takes a settings argument
- **When** `GetStore<TStore, TSettings>(settings)` is called for a new cache entry
- **Then** `Activator.CreateInstance(type, new object[] { })` throws `MissingMethodException` while the
  static lock is held

#### Scenario: Cached instance is read outside the lock

- **Given** one thread inside the `lock (_lockObject)` block adding a new entry to `_stores[id]`
- **When** another thread reaches the final `return (TStore)_stores[id][type];`, which sits after the lock
  block
- **Then** it indexes the non-thread-safe `Dictionary` concurrently with that mutation

### Requirement: StoreLocator applies settings only on first construction, and only through the ISettings interface

The system SHALL call `SetSettings(settings!)` immediately after constructing a new store instance, and only
when the identity string is non-empty and the new instance is assignable to `ISettingsStore<ISettings>`.

#### Scenario: Store implements the ISettings-typed setter

- **Given** `InMemoryStore<T>`, which implements `ISettingsStore<ISettings>`
- **When** it is created through `GetStore` with settings whose `GetId()` is non-empty
- **Then** `SetSettings(ISettings)` is invoked on the fresh instance

#### Scenario: Store only exposes a concretely typed setter

- **Given** a store implementing `ISettingsStore<MySqlSettings>` but not `ISettingsStore<ISettings>`
- **When** it is created through `GetStore` with `MySqlSettings`
- **Then** the `is ISettingsStore<ISettings>` test fails and the store is returned unconfigured, with no
  error

#### Scenario: Settings whose identity is the empty string

- **Given** a settings instance whose `GetId()` returns `string.Empty`
- **When** `GetStore` constructs the store
- **Then** the `!string.IsNullOrEmpty(id)` guard fails and `SetSettings` is skipped, so the store is returned
  unconfigured

#### Scenario: Settings changed after the instance is cached

- **Given** a store already cached under identity `"db1"`
- **When** `GetStore` is called again for `"db1"` with a different settings instance
- **Then** the cached instance is returned as-is; `SetSettings` runs only inside the
  `!_stores[id].ContainsKey(type)` branch

### Requirement: Store wrappers expose their inner store for unwrapping

The system SHALL define `IStoreWrapper` with `object? GetInnerStore()` and
`IStoreWrapper<out T> : IStoreWrapper` (for `T : Models.AbstractModel`) adding
`TStore? GetInnerStoreAs<TStore>() where TStore : class`, so decorator layers such as tenant or audit
wrappers can be traversed.

#### Scenario: Decorator declares its inner store

- **Given** a tenant wrapper around a SQL store
- **When** a caller needs the SQL store's backend-specific API
- **Then** the wrapper's `GetInnerStore()` (or the strongly-typed `GetInnerStoreAs<TStore>()`) surfaces it

### Requirement: GetUnwrappedStore walks the wrapper chain to its innermost store

The system SHALL implement `GetUnwrappedStore(this object? store)` to return `null` for a `null` input,
otherwise loop while the current value implements `IStoreWrapper`, replacing it with `GetInnerStore()`, and
break out — returning the current value rather than `null` — when `GetInnerStore()` yields `null`.
The typed overloads `GetUnwrappedStore<T, TStore>(this IStore<T>?)` and
`GetUnwrappedStore<T, TStore>(this IAsyncStore<T>?)` SHALL run that walk and then apply `as TStore`.

#### Scenario: Three-layer decorator chain

- **Given** a tenant wrapper around a soft-delete wrapper around a `DataBaseStore`
- **When** `store.GetUnwrappedStore<Invoice, DataBaseStore<MyDb, Invoice>>()` is called
- **Then** both wrapper layers are traversed and the `DataBaseStore` is returned

#### Scenario: Wrapper reports a null inner store

- **Given** a wrapper whose `GetInnerStore()` returns `null`
- **When** the walk reaches it
- **Then** the loop breaks and that wrapper instance itself is the "unwrapped" result, which the typed
  overload's `as TStore` then converts to `null` unless the wrapper happens to be a `TStore`

#### Scenario: Requested type is an intermediate layer, not the innermost

- **Given** a chain outer → `SoftDeleteStoreWrapper` → `DataBaseStore`
- **When** `GetUnwrappedStore<T, SoftDeleteStoreWrapper<T>>()` is called
- **Then** `null` is returned, because the walk always continues to the innermost store and only that
  innermost value is type-tested

#### Scenario: No bulk-store overload exists

- **Given** a caller holding an `IBulkStore<T>` or `IAsyncBulkStore<T>`
- **When** it looks for a matching `GetUnwrappedStore<T, TStore>` extension
- **Then** none is declared — only the `IStore<T>` and `IAsyncStore<T>` overloads exist, even though
  `IsStoreOfType` has all four

### Requirement: IsStoreOfType tests the store, then its generic wrapper, then the innermost store

The system SHALL implement `IsStoreOfType<T, TStore>` for `IStore<T>?`, `IAsyncStore<T>?`, `IBulkStore<T>?`
and `IAsyncBulkStore<T>?` with the same three-stage body: `false` for `null`; `true` when the store itself
`is TStore`; otherwise when the store is an `IStoreWrapper<T>`, the result of
`GetInnerStoreAs<TStore>() != null`; otherwise the result of testing the innermost unwrapped store.

#### Scenario: The store is directly the requested type

- **Given** an undecorated `DataBaseStore<MyDb, Invoice>`
- **When** `IsStoreOfType<Invoice, DataBaseStore<MyDb, Invoice>>()` is called
- **Then** `true` is returned on the direct type check, without unwrapping

#### Scenario: Generic wrapper answers for its inner store

- **Given** a wrapper implementing `IStoreWrapper<Invoice>` whose `GetInnerStoreAs<TStore>()` returns a value
- **When** `IsStoreOfType<Invoice, TStore>()` is called
- **Then** `true` is returned from the wrapper branch, and the chain-walking fallback is never reached

#### Scenario: Generic wrapper denies, and the walk is not consulted

- **Given** a store implementing `IStoreWrapper<Invoice>` whose `GetInnerStoreAs<TStore>()` returns `null`
  even though a deeper layer is a `TStore`
- **When** `IsStoreOfType<Invoice, TStore>()` is called
- **Then** `false` is returned — the `IStoreWrapper<T>` branch returns directly, so reaching the innermost
  store depends entirely on that wrapper's own implementation

#### Scenario: Null store

- **Given** a `null` store reference
- **When** `IsStoreOfType<Invoice, TStore>()` is called
- **Then** `false` is returned rather than throwing

### Requirement: StoreException is the store-layer exception type and carries an optional inner exception

The system SHALL declare `Birko.Data.Exceptions.StoreException : Exception` with a `(string message)`
constructor delegating to `(string message, Exception? innerException)` with a `null` inner exception.

#### Scenario: Backend failure wrapped for the caller

- **Given** a driver-level exception raised inside a store implementation
- **When** it is rethrown as `new StoreException("Insert failed", ex)`
- **Then** the message and `InnerException` are both preserved by the base `Exception` constructor

#### Scenario: No CRUD path in this contract raises it

- **Given** the abstract bases, the in-memory stores, `StoreLocator` and `StoreExtensions`
- **When** invalid input is supplied — `null` entities, missing identifiers, unknown keys
- **Then** none of them throws `StoreException`; the contract answers with `Guid.Empty`, `null`, `false` or a
  silent no-op instead, and only reflection failures (`MissingMethodException`) and cancellation
  (`OperationCanceledException`) surface as exceptions
