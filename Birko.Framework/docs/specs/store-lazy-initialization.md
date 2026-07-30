---
area: store-lazy-initialization
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.Stores/AbstractAsyncBulkStore.cs
  - ../Birko.Data.Stores/AbstractAsyncStore.cs
  - ../Birko.Data.Stores/AbstractBulkStore.cs
  - ../Birko.Data.Stores/AbstractStore.cs
shaped-by: []
---

# Lazy store initialization (EnsureInitialized double-checked locking)

## Purpose

Every Birko store — SQL, document, file-based, in-memory — inherits its lifecycle from one of four
abstract base classes: `AbstractStore<T>` / `AbstractBulkStore<T>` (synchronous) and
`AbstractAsyncStore<T>` / `AbstractAsyncBulkStore<T>` (asynchronous). Those bases guarantee that a
concrete store's one-time backend setup (creating tables, indexes, collections, files, connections)
runs exactly once, automatically, immediately before the first data operation that needs it — so a
caller never has to remember to call `Init()`. The mechanism is a private boolean flag guarded by
double-checked locking: a `Monitor` (`lock`) on the sync side, a `SemaphoreSlim` on the async side.

This document records what those four files actually do. The two sides are *not* symmetric: they
differ in cancellation handling, in re-entrancy failure mode, and in what a caller can observe. Those
divergences are the point of this spec, because every concrete store in the framework inherits them.

## Requirements

### Requirement: Automatic initialization before every data operation

The system SHALL invoke `EnsureInitialized()` / `EnsureInitializedAsync(ct)` at the start of each
public data-operation method before delegating to the corresponding `protected abstract *Core` method,
so that a concrete store's `InitCore` / `InitCoreAsync` has completed before any `*Core` method runs.

#### Scenario: First Create on a never-initialized synchronous store

- **Given** a concrete `AbstractStore<T>` subclass that has never had `Init()` called
- **When** the caller invokes `Create(data)`
- **Then** `EnsureInitialized()` runs first, which calls the subclass's `InitCore()`, and only then is `CreateCore(data, storeDelegate)` invoked

#### Scenario: Every single-entity entry point on the sync base initializes

- **Given** a concrete `AbstractStore<T>` subclass
- **When** any of `Create(T)`, `Read(Expression<Func<T,bool>>?)`, `Update(T)`, `Delete(T)` or `Count(Expression<Func<T,bool>>?)` is called first
- **Then** each one calls `EnsureInitialized()` before its `CreateCore` / `ReadCore` / `UpdateCore` / `DeleteCore` / `CountCore` counterpart

#### Scenario: Every single-entity entry point on the async base initializes

- **Given** a concrete `AbstractAsyncStore<T>` subclass
- **When** any of `CreateAsync(T,…)`, `ReadAsync(Expression<Func<T,bool>>?,…)`, `UpdateAsync(T,…)`, `DeleteAsync(T,…)` or `CountAsync(…)` is awaited first
- **Then** each one awaits `EnsureInitializedAsync(ct)` before awaiting its `*CoreAsync` counterpart

#### Scenario: Bulk entry points initialize through the same flag

- **Given** a concrete `AbstractBulkStore<T>` subclass
- **When** any of `Create(IEnumerable<T>)`, `Read(filter, orderBy, limit, offset)`, `Update(IEnumerable<T>)` or `Delete(IEnumerable<T>)` is called first
- **Then** each calls the inherited `EnsureInitialized()` — `AbstractBulkStore<T>` declares no initialization state of its own and reuses the single `_initialized` flag declared in `AbstractStore<T>`

#### Scenario: Async bulk entry points initialize through the same flag

- **Given** a concrete `AbstractAsyncBulkStore<T>` subclass
- **When** any of `CreateAsync(IEnumerable<T>,…)`, `ReadAsync(filter, orderBy, limit, offset, ct)`, `UpdateAsync(IEnumerable<T>,…)` or `DeleteAsync(IEnumerable<T>, ct)` is awaited first
- **Then** each awaits the inherited `EnsureInitializedAsync(ct)` — `AbstractAsyncBulkStore<T>` adds only an empty public constructor and declares no initialization state of its own

### Requirement: InitCore runs exactly once per store instance on success

The system SHALL call `InitCore()` / `InitCoreAsync(ct)` at most once per store instance for as long
as that call succeeds, by setting the private `_initialized` flag to `true` immediately after the core
initialization returns and short-circuiting all subsequent `EnsureInitialized` / `EnsureInitializedAsync`
calls on the flag.

#### Scenario: A hundred sequential operations initialize once

- **Given** a concrete `AbstractStore<T>` subclass whose `InitCore()` increments a counter
- **When** the caller performs `Create`, then `Read`, then `Count`, then `Update`, then `Delete`
- **Then** the counter is `1` — the first call took the `lock`, ran `InitCore()`, and set `_initialized = true`; the four later calls returned at the un-locked `if (_initialized) return;` guard

#### Scenario: Concurrent first-touch initializes once

- **Given** a concrete `AbstractStore<T>` subclass whose `InitCore()` increments a counter and sleeps
- **When** several threads simultaneously call `Count()`
- **Then** exactly one thread runs `InitCore()`; the rest block on `lock (_initLock)`, and on entry each observes `_initialized == true` at the inner re-check and returns without calling `InitCore()` again

#### Scenario: Concurrent first-touch on the async base initializes once

- **Given** a concrete `AbstractAsyncStore<T>` subclass whose `InitCoreAsync()` increments a counter and awaits a delay
- **When** several tasks simultaneously await `CountAsync()`
- **Then** exactly one task passes `await _initLock.WaitAsync(ct)` and runs `InitCoreAsync`; the others queue on the `SemaphoreSlim(1, 1)`, and on acquisition each observes `_initialized == true` at the inner re-check and returns through the `finally` that releases the semaphore

### Requirement: Explicit Init is the same operation as implicit init

The system SHALL implement `Init()` as a bare call to `EnsureInitialized()` and `InitAsync(ct)` as a
direct return of `EnsureInitializedAsync(ct)`, so that explicit initialization is idempotent and shares
one flag with the automatic path — an explicit `Init()` before the first CRUD call causes no second
`InitCore()`, and a redundant `Init()` after CRUD has begun is a no-op.

#### Scenario: Init followed by CRUD

- **Given** a concrete `AbstractStore<T>` subclass whose `InitCore()` increments a counter
- **When** the caller calls `Init()` and then `Create(data)`
- **Then** the counter is `1`

#### Scenario: Init called twice

- **Given** a store on which `Init()` has already succeeded
- **When** `Init()` is called again
- **Then** it returns at the `if (_initialized) return;` guard without taking the lock and without calling `InitCore()`

#### Scenario: InitAsync awaited twice

- **Given** a store on which `await InitAsync()` has already succeeded
- **When** `await InitAsync()` is issued again
- **Then** `InitCoreAsync` is not called again; the returned task completes after only the token check and the flag check

### Requirement: Failed initialization leaves the store uninitialized and is retried

The system SHALL leave `_initialized` at `false` when `InitCore()` / `InitCoreAsync(ct)` throws,
because the assignment `_initialized = true` sits after the core call. The lock is still released (the
sync `lock` block unwinds; the async `finally` calls `_initLock.Release()`), so the exception propagates
to the caller and the *next* data operation attempts initialization again. There is no cached failure,
no failure count, and no backoff.

#### Scenario: Init throws, then the caller retries

- **Given** a concrete `AbstractStore<T>` subclass whose `InitCore()` throws `InvalidOperationException` on its first invocation and succeeds on its second
- **When** the caller calls `Count()`, catches the `InvalidOperationException`, and calls `Count()` again
- **Then** the second `Count()` invokes `InitCore()` a second time, it succeeds, `_initialized` becomes `true`, and `CountCore(null)` runs

#### Scenario: Permanently failing backend throws on every operation

- **Given** a concrete store whose `InitCoreAsync` always throws `StoreException`
- **When** the caller awaits `CreateAsync(a)`, then `CreateAsync(b)`, then `CreateAsync(c)`
- **Then** all three throw `StoreException` and `InitCoreAsync` is invoked three times — the failure is not memoized

#### Scenario: Partially completed initialization is re-run from the start

- **Given** an `InitCoreAsync` that creates table A, then throws while creating table B
- **When** the next data operation runs
- **Then** `InitCoreAsync` is invoked again from the beginning against a backend that already has table A — the base class imposes an unstated requirement that `InitCore` / `InitCoreAsync` tolerate being re-run after partial work, and it neither documents nor enforces it

#### Scenario: The lock is not left held after a failed init

- **Given** a concrete `AbstractAsyncStore<T>` subclass whose `InitCoreAsync` throws
- **When** one task awaits `CountAsync()` and fails, and a second task then awaits `CountAsync()`
- **Then** the second task acquires `_initLock` — the `finally` released it — and runs its own `InitCoreAsync` attempt rather than hanging

### Requirement: Cancellation is observed on every async operation, including already-initialized stores

The system SHALL call `ct.ThrowIfCancellationRequested()` as the first statement of
`EnsureInitializedAsync(ct)`, before the `_initialized` check, so that a cancelled token aborts an async
data operation even when the store is already initialized and no initialization work remains.

#### Scenario: Cancelled token on an already-initialized async store

- **Given** an initialized `AbstractAsyncStore<T>` and a `CancellationToken` that is already cancelled
- **When** the caller awaits `ReadAsync(filter, cancelledToken)`
- **Then** `EnsureInitializedAsync` throws `OperationCanceledException` from `ThrowIfCancellationRequested()` and `ReadCoreAsync` is never reached

#### Scenario: InitAsync with a cancelled token faults the task rather than throwing synchronously

- **Given** a store and an already-cancelled `CancellationToken`
- **When** the caller invokes `InitAsync(cancelledToken)` without awaiting
- **Then** the call itself does not throw — `EnsureInitializedAsync` is an `async` method, so the `OperationCanceledException` is captured on the returned `Task` and surfaces only when that task is awaited or observed

#### Scenario: The synchronous path has no cancellation at all

- **Given** a concrete `AbstractStore<T>` subclass whose `InitCore()` blocks for a long time
- **When** a second thread calls `Count()`
- **Then** that thread blocks on `lock (_initLock)` indefinitely — `EnsureInitialized()` accepts no `CancellationToken`, has no timeout, and offers no way to abandon the wait

### Requirement: A cancelled waiter never acquires or releases the async init lock

The system SHALL perform `await _initLock.WaitAsync(ct)` *outside* the `try` block whose `finally`
releases the semaphore, so that a waiter cancelled while queued throws `OperationCanceledException`
without ever having acquired the lock and without executing a spurious `Release()`.

#### Scenario: Waiter cancelled while another task initializes

- **Given** task A holding `_initLock` inside a slow `InitCoreAsync`, and task B queued on `WaitAsync(ctB)`
- **When** `ctB` is cancelled
- **Then** B's `WaitAsync` throws `OperationCanceledException` before the `try` is entered, so the `finally` does not run and the semaphore count is not corrupted; A continues to hold the lock and completes normally

#### Scenario: A cancelled waiter does not initialize the store

- **Given** the state above
- **When** B's wait is cancelled
- **Then** B's `CountAsync` fails with `OperationCanceledException` even if A's initialization is about to succeed — there is no re-check of `_initialized` after a cancelled wait

### Requirement: Initialization runs under the token of whichever caller wins the race

The system SHALL pass the `CancellationToken` supplied by the caller that acquired the init lock into
`InitCoreAsync(ct)`. The token is neither replaced by `CancellationToken.None` nor combined with the
tokens of queued callers, so which token governs the one-time initialization depends on the arrival
order of concurrent callers.

#### Scenario: Winner's token cancels initialization, loser re-runs it with its own token

- **Given** caller A awaiting `CreateAsync(a, ct: ctA)` and caller B awaiting `CreateAsync(b, ct: ctB)`, with A acquiring the lock first
- **When** `ctA` is cancelled during `InitCoreAsync(ctA)`
- **Then** A's operation throws `OperationCanceledException`, `_initialized` remains `false`, and B — on acquiring the released lock — sees `_initialized == false` and runs `InitCoreAsync(ctB)` itself

#### Scenario: A short-lived token performs initialization for a long-lived caller

- **Given** caller A with a token that has a 50 ms timeout, arriving first, and caller B with `CancellationToken.None`
- **When** `InitCoreAsync` needs 200 ms
- **Then** the initialization actually performed under A's token is aborted at 50 ms, and B then repeats the whole initialization under `None` — the framework does not detach the one-time init from the winner's cancellation scope

### Requirement: Recursive initialization fails differently on the sync and async sides

The system SHALL guard sync initialization with a re-entrant `Monitor` (`lock (_initLock)`) and async
initialization with a non-re-entrant `SemaphoreSlim(1, 1)`. Because `_initialized` is set only *after*
`InitCore` / `InitCoreAsync` returns, a concrete store whose initialization calls back into one of its
own public data operations fails — but the two hierarchies fail in different, backend-visible ways.

#### Scenario: Sync InitCore seeds data through a public CRUD method

- **Given** an `AbstractStore<T>` subclass whose `InitCore()` calls `this.Create(seedRow)` to seed defaults
- **When** the first data operation triggers `EnsureInitialized()`
- **Then** `Create` calls `EnsureInitialized()` again on the same thread, the `lock` is re-entered because `Monitor` is re-entrant, `_initialized` is still `false`, and `InitCore()` is invoked recursively — the recursion is unbounded and terminates in a `StackOverflowException`

#### Scenario: Async InitCoreAsync seeds data through a public CRUD method

- **Given** an `AbstractAsyncStore<T>` subclass whose `InitCoreAsync()` awaits `this.CreateAsync(seedRow)` to seed defaults
- **When** the first data operation awaits `EnsureInitializedAsync()`
- **Then** the nested `EnsureInitializedAsync` sees `_initialized == false` and awaits `_initLock.WaitAsync(ct)` on a semaphore already held by the outer call — `SemaphoreSlim` has no owner-thread affinity, so the wait never completes and the operation deadlocks until the token is cancelled

#### Scenario: Initialization that talks to the backend directly is safe

- **Given** an `InitCoreAsync` that seeds data via its own connection or via the `protected *CoreAsync` methods rather than the public `CreateAsync` wrappers
- **When** initialization runs
- **Then** no nested `EnsureInitializedAsync` occurs and initialization completes normally — bypassing the public wrappers is the only supported way for `InitCore` to touch the store's own data

### Requirement: The initialization flag is read without synchronization

The system SHALL declare `_initialized` as a private, non-`volatile` `bool` in both `AbstractStore<T>`
and `AbstractAsyncStore<T>`, and SHALL read it once outside the lock as the fast path of the
double-checked lock (`if (_initialized) return;` before `lock (_initLock)` / before
`await _initLock.WaitAsync(ct)`). No `volatile`, `Interlocked`, `Thread.MemoryBarrier`, or
`Lazy<T>`/`LazyInitializer` is used.

#### Scenario: Fast path performs an unsynchronized read

- **Given** an initialized store
- **When** any data operation calls `EnsureInitialized()`
- **Then** the method returns after a single plain field read, taking no lock — this is the intended performance property of the design

#### Scenario: The write is published from inside the critical section

- **Given** a thread completing `InitCore()`
- **When** it assigns `_initialized = true`
- **Then** the assignment happens inside `lock (_initLock)` (sync) or between `WaitAsync` and the `finally`'s `Release()` (async), but the *reading* side has no matching acquire barrier — correctness of the unsynchronized read rests on the runtime's memory model rather than on any explicit annotation in the code

### Requirement: Filter-based bulk operations initialize only through their delegated reads

The system SHALL implement the composed bulk operations — `Update(filter, PropertyUpdate<T>)`,
`Update(filter, Action<T>)`, `Delete(filter)`, `Read()`, and their async counterparts — without any
direct `EnsureInitialized` call, relying on the delegated `Read` / `ReadAsync` (and then the per-item
`Update` / `UpdateAsync` or the bulk `Delete` / `DeleteAsync`) to perform initialization.

#### Scenario: Delete by filter on a never-initialized store

- **Given** a never-initialized `AbstractBulkStore<T>` subclass
- **When** the caller invokes `Delete(x => x.Guid == someGuid)`
- **Then** initialization happens inside the `Read(filter, null, null, null)` call that `Delete(filter)` makes first, and the subsequent `Delete(items)` finds `_initialized` already `true`

#### Scenario: PropertyUpdate by filter routes through the Action overload

- **Given** a never-initialized `AbstractAsyncBulkStore<T>` subclass
- **When** the caller awaits `UpdateAsync(filter, propertyUpdate, ct)`
- **Then** it forwards to `UpdateAsync(filter, entity => updates.ApplyTo(entity), ct)`, which awaits `ReadAsync(filter, null, null, null, ct)` — the operation that actually initializes — and then awaits the single-entity `UpdateAsync(item, ct: ct)` once per matched item

#### Scenario: Filter matching nothing still reaches the backend once

- **Given** an initialized bulk store and a filter matching no rows
- **When** `Delete(filter)` is called
- **Then** `Read` returns an empty sequence and `Delete(items)` is still invoked with an empty list, so `DeleteCore(empty)` is called on the concrete store — there is no empty-set short-circuit

#### Scenario: Parameterless bulk Read delegates to the four-argument overload

- **Given** a never-initialized `AbstractBulkStore<T>` subclass
- **When** `Read()` is called
- **Then** it invokes the virtual `Read(null, null, null, null)`, which calls `EnsureInitialized()` and honours any concrete override of the four-argument bulk read

### Requirement: Single-result reads on a bulk store initialize via the hidden base overload

The system SHALL implement `ReadFirst(filter)` as `base.Read(filter)` and `ReadFirstAsync(filter, ct)`
as `base.ReadAsync(filter, ct)`, explicitly reaching the single-result read declared on the non-bulk
base — which the bulk four-argument overload otherwise removes from member lookup — and therefore
initializing through `AbstractStore<T>.Read` / `AbstractAsyncStore<T>.ReadAsync`.

#### Scenario: ReadFirst on a never-initialized bulk store

- **Given** a never-initialized `AbstractBulkStore<T>` subclass
- **When** `ReadFirst(x => x.Guid == g)` is called
- **Then** `base.Read(filter)` runs `EnsureInitialized()` and then `ReadCore(filter)` — the single-result core, not the bulk `ReadCore(filter, orderBy, limit, offset)`

#### Scenario: Read by GUID resolves to the single-result path

- **Given** a never-initialized store
- **When** `Read(guid)` (sync) or `ReadAsync(guid, ct)` (async) is called
- **Then** it builds `new Filters.ModelByGuid<T>(guid).Filter()` and forwards to the single-result `Read(filter)` / `ReadAsync(filter, ct)`, which performs the initialization — the GUID overloads contain no `EnsureInitialized` call of their own

### Requirement: Destroy is not wrapped and cannot reset the initialization flag

The system SHALL declare `Destroy()` and `DestroyAsync(ct)` as bare `public abstract` members with no
base-class wrapper: they do not call `EnsureInitialized` / `EnsureInitializedAsync`, and they cannot
clear `_initialized` because the field is `private` with no protected accessor or reset method. A store
therefore remains permanently flagged as initialized after being destroyed, and no subsequent operation
will re-run `InitCore` / `InitCoreAsync`.

#### Scenario: Destroy then reuse skips re-initialization

- **Given** an initialized store whose `Destroy()` drops its backing table
- **When** the caller calls `Destroy()` and then `Create(data)`
- **Then** `EnsureInitialized()` returns immediately because `_initialized` is still `true`, `InitCore()` is not re-run, the table is not recreated, and `CreateCore` runs against the destroyed backend

#### Scenario: Destroy on a never-initialized store

- **Given** a store on which no data operation has ever run
- **When** `Destroy()` (or `await DestroyAsync(ct)`) is called
- **Then** the concrete implementation executes with the backend never having been set up, because no base-class wrapper initializes first

#### Scenario: A subclass cannot re-arm initialization

- **Given** a concrete store that wants `Destroy()` to make the next operation re-initialize
- **When** it looks for a way to reset the flag
- **Then** none exists — `_initialized` and `_initLock` are private in both `AbstractStore<T>` and `AbstractAsyncStore<T>`, and no `protected` `IsInitialized` property, reset method, or virtual hook is exposed

### Requirement: Non-data members never initialize the store

The system SHALL not initialize the store from `CreateInstance()`, nor from `Save` / `SaveAsync` when
the supplied entity is `null`.

#### Scenario: CreateInstance on a never-initialized store

- **Given** a never-initialized store
- **When** `CreateInstance()` is called
- **Then** it returns `Activator.CreateInstance<T>()` (falling back to `Activator.CreateInstance(typeof(T), Array.Empty<object>())` on `MissingMethodException`) without calling `EnsureInitialized()` and without touching the backend

#### Scenario: Saving null never touches the backend

- **Given** a never-initialized store
- **When** `Save(null)` (or `await SaveAsync(null)`) is called
- **Then** it returns `Guid.Empty` at the null guard, so neither `Create`/`CreateAsync` nor `Update`/`UpdateAsync` runs and initialization does not occur

#### Scenario: Save routes initialization through Create or Update

- **Given** a never-initialized store
- **When** `Save(entity)` is called with `entity.Guid` `null` or `Guid.Empty`
- **Then** it delegates to `Create(entity, storeDelegate)`, which is the call that initializes; when `entity.Guid` has a non-empty value it delegates to `Update(entity, storeDelegate)` instead and returns `entity.Guid.Value`

### Requirement: The async initialization lock is never disposed

The system SHALL allocate a `SemaphoreSlim(1, 1)` per `AbstractAsyncStore<T>` instance as a
`private readonly` field and SHALL NOT dispose it — neither `AbstractAsyncStore<T>` nor
`AbstractAsyncBulkStore<T>` implements `IDisposable` or `IAsyncDisposable`, and no `Dispose` path
releases the semaphore.

#### Scenario: Many short-lived async stores are created

- **Given** code that constructs one `AbstractAsyncStore<T>` subclass instance per request
- **When** thousands of such instances become garbage
- **Then** each carries an undisposed `SemaphoreSlim`, reclaimed only by the garbage collector; the synchronous `AbstractStore<T>` has no such concern because its `_initLock` is a plain `object`

#### Scenario: DestroyAsync does not release the semaphore

- **Given** an async store that has finished its lifetime
- **When** `await DestroyAsync(ct)` completes
- **Then** `_initLock` is still undisposed — `DestroyAsync` is abstract with no base-class behaviour and the field is private and inaccessible to the subclass
