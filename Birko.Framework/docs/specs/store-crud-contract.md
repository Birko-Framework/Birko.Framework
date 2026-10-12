---
area: store-crud-contract
generated-at: c50ed580c4d08391a4ae55a7e8b62c394f6e3469
generated-on: 2026-10-12
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
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
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

### Requirement: An update of an entity that is not stored throws

The system SHALL, on every provider, make `Update` / `UpdateAsync` of an entity whose `Guid` matches no stored
entity — or that has no `Guid` — throw `EntityNotFoundException` (a `StoreException` carrying `EntityType` and
`Guids`) and write nothing. Each provider SHALL detect the miss from its own native signal, never a pre-read: the
in-memory and file stores their key lookup, SQL the rows the `UPDATE` matched, MongoDB `MatchedCount` (acknowledged
writes only), RavenDB the load it already performs, CosmosDB an item-level 404 (sub-status 1003, a missing container,
still propagates as `CosmosException`), Elasticsearch `document_missing_exception`. A bulk `Update` SHALL write every
entity that is stored and then throw one `EntityNotFoundException` naming the rest (SH-M306, fixed by TASK-555 — every
backend used to write nothing and report success; RavenDB and the async XML bulk update instead created the entity).
**InfluxDB is the exception:** a point write has no matched count and overwrites by series key and time, so its
`Update` writes the point (an upsert); detecting the miss would cost a query per write.

#### Scenario: Updating an entity that is not stored

- **Given** a store holding one entity, and an entity carrying a `Guid` no stored record uses
- **When** `Update(entity)` is called
- **Then** `EntityNotFoundException` is thrown with `Guids` = that `Guid`, and the store still holds one entity
  (the cross-provider `UpdateMissContract`, run on in-memory, JSON, XML ×3, SQLite, PostgreSQL, MySQL, SQL Server,
  TimescaleDB, MongoDB, RavenDB, CosmosDB and Elasticsearch, sync and async)

#### Scenario: Bulk update with one missing entity

- **Given** two stored entities, renamed, and one entity that is not stored, in one batch
- **When** bulk `Update(batch)` runs
- **Then** both stored entities are written and `EntityNotFoundException` names only the missing one

#### Scenario: A model with no declared primary key on a SQL bulk path

- **Given** a SQL-mapped model with no `[PrimaryField]` (the ordinary `AbstractModel`)
- **When** a native bulk `Update` or `Delete` runs on SQLite, PostgreSQL, MySQL or SQL Server
- **Then** it keys on `Guid` through `DataBase.GetPrimaryFields`, like every single-item path — the bulk paths used
  `Table.GetPrimaryFields()`, which has no fallback, and silently updated or deleted nothing (TASK-555,
  `BulkPrimaryKeyFallbackTests`)

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

#### Scenario: A CRUD call made from inside InitCore passes through

- **Given** an `InitCore()` / `InitCoreAsync(ct)` that seeds defaults through `this.Create(...)` /
  `await CreateAsync(...)`
- **When** the store initializes
- **Then** the nested call's `EnsureInitialized` sees that **its own call flow** is running this store's init
  (a per-instance `AsyncLocal<bool>`) and returns at once, so `InitCore` runs once and the seed is stored —
  in both bases alike. Another thread or flow still waits for the init to finish. TASK-163 (SH-M316): before,
  the sync base's re-entrant `Monitor` let the nested call in to run `InitCore` again until a
  `StackOverflowException`, and the async base's `SemaphoreSlim` (no owner affinity) deadlocked — one mistake,
  two unrelated symptoms

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
reported; and otherwise to call `Update` / `UpdateAsync` and return `data.Guid!.Value` — **creating the entity under
that Guid when the update reports `EntityNotFoundException` for it** (an upsert). The decision has one producer,
`StoreSave`, used by both base stores and every decorator that routes it (timestamp, audit, soft-delete, sluggable,
default, versioned, localization, event sourcing, tenant), each passing its own create so its create behaviour runs.
The store delegate runs once on the upsert path. MongoDB, RavenDB and CosmosDB `SaveAsync` upsert natively.

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
- **When** `Save(entity)` is called, on any provider or through any routing decorator
- **Then** the entity is created under that `Guid` and `Save` returns it (SH-M309, fixed by TASK-555 — `Save` used to
  return the `Guid` of an entity it never stored)

#### Scenario: The upsert runs the delegate once

- **Given** an entity with an unstored `Guid` and a store delegate
- **When** `Save(entity, delegate)` updates, misses, and creates
- **Then** the delegate has run exactly once

#### Scenario: A provider SaveAsync override honours the store contract

- **Given** `AsyncMongoDBStore` / `AsyncCosmosDBStore`, whose `SaveAsync` overrides the public wrapper
- **When** `SaveAsync` updates an existing entity, or is the first call on a store
- **Then** the delegate runs (MongoDB skipped it on the update branch) and the lazy initialization runs first
  (CosmosDB returned `Guid.Empty` and wrote nothing) — both found by the contract, fixed by TASK-555

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

### Requirement: Destroy permanently deletes stored data and is documented as such

The system SHALL implement `Destroy()` / `DestroyAsync(ct)` in the bases as **non-virtual** wrappers that
call the abstract `DestroyCore()` / `DestroyCoreAsync(ct)` without opening the initialization gate, and then
**reset the private `_initialized` flag** — also when the core throws (TASK-163, SH-M307; rule 12). A
`DestroyCore` override that calls the public `Destroy` again on the same flow SHALL be refused with
`InvalidOperationException` naming `base.DestroyCore` rather than recursing.

The XML documentation on `IBaseStore.Destroy()` and `IAsyncBaseStore.DestroyAsync(ct)` SHALL state
that the operation **permanently deletes every row the store can see and is not disposal**, SHALL
give the per-backend blast radius, and SHALL name the alternatives a caller has. SH-H046: it
previously read *"Destroys the store and releases all resources"*, which is disposal wording, so the
declared and the implemented meaning disagreed and a teardown-style call destroyed production data.

The blast radius is per backend and is wider than the entity on two of them: RavenDB sends
`DeleteDatabasesOperation(hardDelete: true)` and drops the **entire database**; CosmosDB deletes the
container; MongoDB drops the collection; SQL drops the table; JSON and XML delete the file; the
in-memory stores clear `_items`. Nothing is recoverable and no overload accepts a filter.

⚠ The documentation SHALL redirect resource release to `IDisposable`, which **the contract itself
does not declare** — `IBaseStore`, `IStore<T>`, `IAsyncBaseStore`, `IAsyncStore<T>` and the four
abstract bases carry no disposal member, so `Destroy` was the only cleanup-looking member a caller
could find on the contract. Disposal is a per-implementation concern and **does exist** where
resources do: `RavenDBStore` and `InfluxDBStore` declare `IDisposable`, and `DataBaseStore`,
`AsyncDataBaseStore`, `AsyncRavenDBStore`, `AsyncInfluxDBStore` and `CachedAsyncDataBaseBulkStore`
reference it. That is what made the old wording actively harmful rather than merely vague: it
described what an existing, correct member already did.

#### Scenario: The documentation states what the operation destroys

- **Given** `IBaseStore.Destroy()` or `IAsyncBaseStore.DestroyAsync(ct)`
- **When** a caller reads the summary
- **Then** it says the operation permanently deletes every row and is not disposal, before anything else — a warning placed below a reassuring summary is the shape that caused SH-H046

#### Scenario: The documentation names the alternatives

- **Given** a caller who wanted resource cleanup, selective deletion, or an emptied but usable store
- **When** they read the remarks
- **Then** they are pointed at `IDisposable` where the store implements it, `IBulkDeleteStore<T>.Delete(filter)` / `IAsyncBulkStore<T>.DeleteAsync(filter)`, and `DeleteAll()` / `DeleteAllAsync()` — the last stated as base-class members on `AbstractBulkStore<T>` / `AbstractAsyncBulkStore<T>` rather than interface members, because they are not on any interface

#### Scenario: No implementation changed (SH-H046 only)

- **Given** the 17 `Destroy` / `DestroyAsync` overrides across the backends
- **When** SH-H046 is fixed
- **Then** none of them changes (TASK-163 later renamed every override to `DestroyCore` / `DestroyCoreAsync`
  for the latch reset — a separate change) — they were behaving correctly and were described wrongly, so the fix is entirely on the contract's documentation

#### Scenario: Destroying an in-memory store

- **Given** an initialized `AbstractInMemoryStore<T>` holding entities
- **When** `Destroy()` is called
- **Then** `_items.Clear()` empties the backing dictionary and the store keeps serving CRUD calls against the
  now-empty dictionary

#### Scenario: CRUD after Destroy initializes again

- **Given** any store whose `InitCore()` provisions backend structures and whose `DestroyCore()` removes them
- **When** a CRUD method is invoked after `Destroy()` — including after a `Destroy()` that threw
- **Then** `_initialized` was reset by the `Destroy` wrapper, so `EnsureInitialized()` runs `InitCore()` again
  and the storage is provisioned before the operation. Before TASK-163 the flag stayed `true` and the
  operation ran against torn-down storage (SH-M307)

### Requirement: A store persists what the store data delegate returns

The system SHALL declare `StoreDataDelegate<T>(T data)` as returning `T` and SHALL, at every invocation site on every
provider and decorator, persist what the delegate returns through the one helper `StoreDelegates.Apply` — the original
entity when the delegate returns `null`, and a returned replacement with no Guid of its own inherits the original's
(identity is assigned and checked before the delegate runs). On a bulk path whose collection is what gets written, the
collection takes the replacement. (SH-M315, fixed by TASK-557 — all ~110 call sites used to discard the return and
persist the original, so a delegate in the functional style was silently ignored.)

#### Scenario: Delegate stamps a field

- **Given** a delegate `d => { d.Name = "x"; return d; }`
- **When** `Create(entity, delegate)` runs on any provider
- **Then** the mutation is stored (contract pin `Contract_A_delegate_that_mutates_in_place_still_works`)

#### Scenario: Delegate returns a different instance

- **Given** a delegate that builds and returns a new, modified copy of the entity
- **When** `Create`, `Update`, bulk `Create` or bulk `Update` runs, sync or async, on any provider
- **Then** the returned copy is what is stored (`DelegateContract`, run on every provider — it used to be thrown away)

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

#### Scenario: The store keeps copies, like a real backend

- **Given** an `InMemoryStore<T>` with `SnapshotEntities` at its default `true`
- **When** an entity is created and the caller then mutates its own object, or mutates an object a read returned
- **Then** a fresh read still returns the stored state — the store keeps and hands out detached copies (a JSON
  round-trip, overridable through `Snapshot`), as every serializing backend does. Only `Update` changes stored state
  (SH-M311, fixed by TASK-557 — it used to store and return the caller's live reference, so a missing `Update` passed
  on the canonical test double). `SnapshotEntities = false` restores shared references for a model that cannot
  round-trip.

#### Scenario: No initialization work is required

- **Given** a new `AsyncInMemoryStore<Invoice>`
- **When** the initialization gate calls `InitCoreAsync(ct)`
- **Then** `Task.CompletedTask` is returned without touching the token or the dictionary

### Requirement: Every store assigns an identity through one rule, single or bulk

The system SHALL give an entity its identity on create, on every provider and on both the single and the bulk path,
through `AbstractModel.EnsureGuid()` alone: a new `Guid` when `Guid` is `null` **or** `Guid.Empty`, and any other value
kept as the caller supplied it. No store re-implements the rule (SH-M313 / SH-M314, fixed by TASK-556). Before it, ~55
sites followed three policies: `??=` kept `Guid.Empty` as a real key (stored under it, then unreachable through the
in-memory `Read(Guid)`), two paths replaced it, and every SQL (SQLite, PostgreSQL, MySQL, SQL Server), TimescaleDB and
RavenDB **bulk** create overwrote the caller's Guid — CR-L190's recorded decision, reversed on the user's decision of
2026-10-11 — so entities linked by pre-set ids were stored under different ones on the default provider.

#### Scenario: Bulk create keeps the ids the caller chose

- **Given** a parent and a child carrying caller-chosen Guids
- **When** they are bulk-created on any provider
- **Then** both are stored under those Guids (`IdentityContract`, run on every provider, sync and async)

#### Scenario: An empty Guid is never a key

- **Given** an entity whose `Guid` is `Guid.Empty`, created alone or in a bulk batch
- **When** it is created
- **Then** it receives a fresh `Guid`, and a read of `Guid.Empty` finds nothing — so the in-memory `Read(Guid)` refusing
  `Guid.Empty` and the base `Read(Guid)` filtering on it agree

### Requirement: In-memory create assigns a missing identifier and refuses an existing key

The system SHALL, in `CreateCore` / `CreateCoreAsync`, return `Guid.Empty` without storing anything when
`data` is `null`; otherwise assign the identity with `data.EnsureGuid()`, refuse a key already stored with
`StoreException` **before** invoking the delegate, invoke the delegate, add the entity with `TryAdd` (a concurrent
create of the same key also throws), and return `data.Guid.Value`. A create never replaces a stored entity
(SH-M310, [[TASK-163]]): a real backend's primary key rejects the duplicate, and the canonical test double does too.

#### Scenario: Creating an entity with no identifier

- **Given** an entity whose `Guid` is `null`
- **When** `Create(entity)` is called
- **Then** a fresh `Guid` is generated, assigned onto the entity, used as the dictionary key, and returned

#### Scenario: Creating over an existing key

- **Given** `_items` already contains an entity under `g`
- **When** `Create(other)` is called with `other.Guid == g`
- **Then** `StoreException` ("Create does not replace — use Update") is thrown, the delegate is not invoked, and
  the stored entity is unchanged

#### Scenario: Creating null

- **Given** `data` is `null`
- **When** `Create(null)` is called
- **Then** `Guid.Empty` is returned and `_items` is unchanged — a value no successful create returns any more, since
  `Guid.Empty` is never a key (TASK-556)

#### Scenario: Creating an entity whose identifier is already Guid.Empty

- **Given** an entity whose `Guid` is `Guid.Empty` rather than `null`
- **When** `Create(entity)` is called
- **Then** it receives a fresh `Guid`, is stored under it, and that `Guid` is returned (it used to be stored under the
  `Guid.Empty` key, unreachable by identifier — SH-M313 / SH-M314, TASK-556)

### Requirement: In-memory read by identifier is an O(1) lookup that rejects the empty Guid

The system SHALL override the public `Read(Guid)` / `ReadAsync(Guid, ct)` in the in-memory stores to open the
initialization gate and then return a `Snapshot` of `_items[guid]` only when `guid != Guid.Empty` and the key
exists, otherwise `null` — bypassing the base classes' `ModelByGuid` filter path.

#### Scenario: Existing entity fetched by key

- **Given** `_items` contains an entity under `g`
- **When** `Read(g)` is called
- **Then** `TryGetValue` finds it directly, with no predicate compilation or scan, and a detached copy of it is
  returned (the stored instance itself when `SnapshotEntities` is `false`)

#### Scenario: Unknown identifier

- **Given** `g` is not a key in `_items`
- **When** `Read(g)` is called
- **Then** `null` is returned

#### Scenario: Empty Guid is answered without a lookup

- **Given** an in-memory store, whose create paths give an entity carrying `Guid.Empty` a fresh `Guid`
  (`EnsureGuid`, TASK-556), so no entity is stored under the `Guid.Empty` key through the store's API
- **When** `Read(Guid.Empty)` is called on the in-memory store
- **Then** the `guid != Guid.Empty` guard short-circuits and `null` is returned without consulting `_items` —
  the same answer the base `AbstractStore<T>.Read(Guid)` reaches by filtering on `x => x.Guid == Guid.Empty`,
  which no stored entity satisfies. (Before TASK-556 an entity could be stored under that key, and the two
  diverged: the base found it, the in-memory override did not)

### Requirement: In-memory single-entity read returns the first match in dictionary order

The system SHALL implement `ReadCore(filter)` / `ReadCoreAsync(filter, ct)` as
`_items.Values.FirstOrDefault(x => predicate?.Invoke(x) ?? true)`, compiling the filter expression with
`filter?.Compile()`, and returning a `Snapshot` of the match.

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

### Requirement: In-memory update refuses an entity it cannot act on; delete is silent

The system SHALL, in `UpdateCore` / `UpdateCoreAsync`, throw `EntityNotFoundException` when `data.Guid` is `null` or
not a key of `_items`, before invoking the delegate; and SHALL then replace the stored entity **only while one is
stored** — `TryGetValue` + `TryUpdate`, retried on a concurrent replacement — throwing `EntityNotFoundException` when a
concurrent removal ends it (SH-M308, [[TASK-163]]; the throw is TASK-555). It SHALL, in `DeleteCore` /
`DeleteCoreAsync`, call `_items.TryRemove` when `data?.Guid != null`, discarding the result.

#### Scenario: Updating an entity that is not stored

- **Given** an entity with a non-empty `Guid` absent from `_items`
- **When** `Update(entity)` is called
- **Then** `EntityNotFoundException` is thrown and nothing is written (it used to return silently)

#### Scenario: Updating an entity with no identifier

- **Given** an entity whose `Guid` is `null`
- **When** `Update(entity)` is called
- **Then** `EntityNotFoundException` is thrown, the delegate is never invoked, and nothing is stored

#### Scenario: The entity is deleted while the update runs

- **Given** a stored entity, and a store delegate (or another thread) that deletes it after the existence check
- **When** `Update(entity, delegate)` writes
- **Then** the entity stays deleted — the write was a `TryUpdate` against the stored value, so it cannot re-insert a
  removed key (before SH-M308 it resurrected it) — and the lost race is reported as `EntityNotFoundException`

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
supplied, then `Select(Snapshot).ToList()` so the result is materialized, as detached copies, before return.

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
(and their async twins); SHALL skip `null` items in bulk create and bulk update; SHALL restrict bulk update's
writes to items with `Guid.HasValue` whose key already exists in `_items`, replacing each as the single-item update
does, and SHALL then throw one `EntityNotFoundException` naming every other item (an item with no `Guid` is named
as `Guid.Empty`) — so bulk update does fail, after writing the stored items (TASK-555); SHALL
refuse a bulk create containing a key already stored, or a key repeated inside the batch, with `StoreException`
**before writing anything**; and SHALL restrict bulk delete to items with `Guid.HasValue`.

#### Scenario: Bulk create over a collection containing nulls

- **Given** a collection of three entities of which one element is `null`
- **When** bulk `Create(data)` runs
- **Then** the `null` is filtered out, the other two receive generated identifiers where missing, and both
  are added

#### Scenario: Bulk create with one key already stored

- **Given** a store holding an entity under `g`, and a batch of a fresh entity plus one carrying `g`
- **When** bulk `Create(data)` runs
- **Then** `StoreException` is thrown and **neither** entity is written — the whole batch is checked first

#### Scenario: Bulk update where only some items exist

- **Given** a collection of four entities, two of which have identifiers present in `_items`
- **When** bulk `Update(data)` runs
- **Then** the two present entities are written, the delegate is not invoked for the other two, and
  `EntityNotFoundException` names the two missing ones (it used to report nothing)

#### Scenario: Bulk operation with a null collection

- **Given** `data` is `null`
- **When** bulk `Create(null)`, `Update(null)` or `Delete(null)` runs
- **Then** the method returns immediately (`Task.CompletedTask` in the async store) leaving `_items` untouched

### Requirement: In-memory filter-based delete is the base class's, so it runs both guards and reaches DeleteCore

The in-memory stores SHALL NOT override the public `Delete(Expression<Func<T, bool>> filter)` /
`DeleteAsync(filter, ct)`. The base `AbstractBulkStore<T>.Delete(filter)` / `AbstractAsyncBulkStore<T>.DeleteAsync`
runs **two** guards — `RequireFilter` (the filter is present) and `RequireBoundedFilter` (it does not cover every
row) — then reads the matches into a list and routes them through `Delete(items)` → `DeleteCore`, so a subclass
overriding `DeleteCore` (the documented extension point) sees filter-based deletes too.

Until [[TASK-163]] (SH-M312) the in-memory stores overrode the public method and removed straight from the
dictionary, bypassing `DeleteCore` — against the family convention that concrete stores override `protected *Core`
and not the public CRUD methods — and so had to repeat both guards themselves (SH-M023, TASK-215).

**The scope guard is not redundant with the null guard, and in this store it is the more dangerous of the
two.** The predicate here is compiled and run as an ordinary C# delegate, with no translation layer
between the caller's expression and the deletion — so a filter such as `!empty.Contains(x.Field)` is
simply *true for every entity*, by definition rather than by mistranslation. Measured before the guard was
added: **0 of 3 rows left, and no exception**. Nothing downstream could have observed it, because nothing
downstream is involved.

#### Scenario: Deleting all entities matching a predicate

- **Given** ten stored entities of which four match the filter
- **When** `Delete(filter)` is called
- **Then** exactly those four are removed, through `DeleteCore`; the matches were read into a list before any
  removal, so the dictionary is not mutated during its own enumeration

#### Scenario: Filter matches nothing

- **Given** no stored entity satisfies the filter
- **When** `Delete(filter)` is called
- **Then** the match list is empty, no removal occurs, and no exception is raised

#### Scenario: A null filter is refused rather than clearing the whole collection

- **Given** an in-memory store holding entities
- **When** `Delete(null!)` / `DeleteAsync(null!, ct)` is called
- **Then** the base guard throws `ArgumentNullException` naming the `filter` parameter, and **no** entity is
  removed — a compiled null predicate would otherwise have matched everything

#### Scenario: A filter that is present but covers every entity is refused

- **Given** an in-memory store holding three entities and an empty collection `empty`
- **When** `Delete(x => !empty.Contains(x.Field))` is called
- **Then** the base guard throws `WholeTableWriteException` naming the deliberate door, and all three
  entities remain — the predicate is true of every entity, so before the guard this left **0 of 3** rows
  with no exception raised

#### Scenario: Every row is still reachable deliberately

- **Given** a caller that genuinely wants the collection emptied
- **When** they call `DeleteAll()` (or pass an explicit `x => true`)
- **Then** the operation proceeds — the guards remove the *accidental* whole-collection delete, not the
  deliberate one. `x => true` is a single explicit constant node and is recognised as the synonym, whereas
  a predicate that merely *reduces* to always-true is refused

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

The system SHALL maintain a process-wide cache keyed first by `settings?.GetId() ?? string.Empty` and then by
`typeof(TStore)`; SHALL create a missing entry with `Activator.CreateInstance(type, Array.Empty<object>())`; and SHALL
return the cached instance on subsequent calls, reading it **inside** the lock (SH-M303). `TStore` is unconstrained
and checked at run time: a type implementing neither `IBaseStore` nor `IAsyncBaseStore` is refused with
`ArgumentException`, and an async store is served like a sync one (SH-M304 — the old `where TStore : IBaseStore`
excluded the whole async family). `GetStore<TStore>()` SHALL delegate to `GetStore<TStore, ISettings>(default!)`,
which resolves to the empty-string identity bucket.

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

#### Scenario: An async store is located

- **Given** `AsyncInMemoryStore<T>`, which implements `IAsyncBaseStore` and not `IBaseStore`
- **When** `GetStore<AsyncInMemoryStore<T>, TSettings>(settings)` is called twice
- **Then** it compiles, and the same instance is returned both times

#### Scenario: A type that is not a store

- **Given** a model type
- **When** it is passed as `TStore`
- **Then** `ArgumentException` ("is not a store") is thrown before anything is constructed

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

#### Scenario: A fresh settings instance with the same values

- **Given** a store already cached under identity `"db1"`
- **When** `GetStore` is called again for `"db1"` with a new settings instance whose value-typed public properties
  are all equal to the cached one's
- **Then** the cached instance is returned; `SetSettings` is not called again

#### Scenario: Settings that share the identity but differ in a value

- **Given** a store cached under `"loc:db"` for `PasswordSettings` with password `a`
- **When** `GetStore` is called for the same type with `PasswordSettings` `"loc:db"`, password `b` — `GetId()` leaves
  the password out, so the identities collide
- **Then** `InvalidOperationException` ("configured with different settings") is thrown, instead of returning a
  store running on the first caller's credentials (SH-M305). Only public properties of value-like types (primitives,
  enums, strings, `decimal`, `TimeSpan`, `DateTime(Offset)`, `Guid`, and their nullable forms) are compared

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

### Requirement: IsStoreOfType tests the store, then the innermost store of its wrapper chain

The system SHALL implement `IsStoreOfType<T, TStore>` for `IStore<T>?`, `IAsyncStore<T>?`, `IBulkStore<T>?`
and `IAsyncBulkStore<T>?` with one body: `false` for `null`; otherwise `true` when the store itself `is TStore`
or the innermost store `GetUnwrappedStore` reaches `is TStore` — the store a backend repository then uses.

#### Scenario: The store is directly the requested type

- **Given** an undecorated `DataBaseStore<MyDb, Invoice>`
- **When** `IsStoreOfType<Invoice, DataBaseStore<MyDb, Invoice>>()` is called
- **Then** `true` is returned on the direct type check, without unwrapping

#### Scenario: A chain of two wrappers

- **Given** a wrapper around a wrapper around an `ElasticSearchStore<Invoice>` — what `StoreWrapperBuilder` builds
- **When** `IsStoreOfType<Invoice, ElasticSearchStore<Invoice>>()` is called, as every backend repository's
  constructor does
- **Then** `true` is returned. Until [[TASK-163]] (SH-M302) the first wrapper's one-level `GetInnerStoreAs`
  answered — with the second wrapper, so `false` — and the repository rejected a correctly composed store

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

#### Scenario: The two CRUD paths in this contract that raise it

- **Given** the abstract bases, the in-memory stores, `StoreLocator` and `StoreExtensions`
- **When** invalid input is supplied
- **Then** an in-memory create of a key already stored throws `StoreException`, and an in-memory update (single or
  bulk) of an entity with no `Guid` or an unstored one throws its subclass `EntityNotFoundException` ([[TASK-555]]);
  a `null` entity, a read or delete of an unknown key, and a delete with no identifier are still answered with
  `Guid.Empty`, `null`, `false` or a silent no-op. `StoreLocator` refuses with `ArgumentException` (not a store) and
  `InvalidOperationException` (colliding settings); reflection failures (`MissingMethodException`) and
  cancellation (`OperationCanceledException`) surface as before
