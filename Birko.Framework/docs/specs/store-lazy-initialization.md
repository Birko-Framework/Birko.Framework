---
area: store-lazy-initialization
generated-at: c50ed580c4d08391a4ae55a7e8b62c394f6e3469
generated-on: 2026-10-12
sources:
  - ../Birko.Data.Stores/AbstractAsyncBulkStore.cs
  - ../Birko.Data.Stores/AbstractAsyncStore.cs
  - ../Birko.Data.Stores/AbstractBulkStore.cs
  - ../Birko.Data.Stores/AbstractStore.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
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
differ in cancellation handling and in what a caller can observe. Those
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
as that call succeeds and the store keeps the default answers of `CanRememberInitialization` and
`CanTrustRememberedInitialization` (both `true`), by setting the private `_initialized` flag to
`CanRememberInitialization` immediately after the core initialization returns and short-circuiting all
subsequent `EnsureInitialized` / `EnsureInitializedAsync` calls on `_initialized && CanTrustRememberedInitialization`.

#### Scenario: A hundred sequential operations initialize once

- **Given** a concrete `AbstractStore<T>` subclass whose `InitCore()` increments a counter
- **When** the caller performs `Create`, then `Read`, then `Count`, then `Update`, then `Delete`
- **Then** the counter is `1` — the first call took the `lock`, ran `InitCore()`, and set `_initialized = CanRememberInitialization` (`true`); the four later calls returned at the un-locked `if (_initialized && CanTrustRememberedInitialization) return;` guard

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
- **Then** it returns at the `if (_initialized && CanTrustRememberedInitialization) return;` guard without taking the lock and without calling `InitCore()`

#### Scenario: InitAsync awaited twice

- **Given** a store on which `await InitAsync()` has already succeeded
- **When** `await InitAsync()` is issued again
- **Then** `InitCoreAsync` is not called again; the returned task completes after only the token check and the flag check

### Requirement: Failed initialization leaves the store uninitialized and is retried

The system SHALL leave `_initialized` at `false` when `InitCore()` / `InitCoreAsync(ct)` throws,
because the assignment `_initialized = CanRememberInitialization` sits after the core call. The lock is still released (the
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

### Requirement: A store may decline to remember, or to trust, a completed initialization

The system SHALL expose two `protected virtual bool` hooks on both `AbstractStore<T>` and
`AbstractAsyncStore<T>`, each defaulting to `true`: `CanRememberInitialization`, read once after
`InitCore` / `InitCoreAsync` returns and assigned to `_initialized` (TASK-244), and
`CanTrustRememberedInitialization`, read on every operation alongside `_initialized` both at the
un-locked fast path and at the re-check inside the lock (TASK-288). A store answering `false` to either
re-runs `InitCore` / `InitCoreAsync` on its next operation instead of short-circuiting — the design errs
toward an idempotent re-run over a store that stays broken for the life of the process.

#### Scenario: An initialization that is not remembered is re-run

- **Given** a concrete store whose `CanRememberInitialization` returns `false` (for example a SQL store whose schema-ensure ran inside a caller's transaction that may still roll back)
- **When** `InitCore()` completes and a second data operation runs
- **Then** `_initialized` was assigned `false`, so the second operation takes the lock and invokes `InitCore()` again

#### Scenario: A remembered initialization that can no longer be trusted is re-run

- **Given** an initialized store whose `_initialized` is `true` and whose `CanTrustRememberedInitialization` now returns `false` (for example a SQL store whose table was dropped underneath it)
- **When** the next data operation calls `EnsureInitialized()` / `EnsureInitializedAsync(ct)`
- **Then** neither the fast path nor the re-check inside the lock returns, and `InitCore()` / `InitCoreAsync(CancellationToken.None)` runs again under the lock

#### Scenario: Default stores pay no re-check cost

- **Given** a store that overrides neither hook
- **When** it is initialized and used
- **Then** both hooks answer `true` and the store behaves as a remember-once store

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

### Requirement: Initialization runs under no caller's token

The system SHALL run `InitCoreAsync` under `CancellationToken.None`, on the flow of the caller that
acquired the init lock, and SHALL observe that caller's own token only after the initialization and the
lock release — so one-time setup that every caller shares is never aborted by one caller's cancellation,
and still participates in that caller's transaction boundary rather than running detached from it
(SH-M425, fixed by TASK-324; it used to run under the winner's token, so arrival order decided which
caller's timeout governed the DDL).

#### Scenario: A short-lived token does not abort initialization for everyone

- **Given** caller A with a token that is cancelled while `InitCoreAsync` is running, arriving first, and caller B with `CancellationToken.None` queued behind it
- **When** the initialization finishes
- **Then** it has run exactly once and to completion, A's operation throws `OperationCanceledException` afterwards, and B proceeds without repeating it

#### Scenario: Init code cannot be cancelled by a caller

- **Given** any caller token
- **When** `InitCoreAsync(ct)` runs
- **Then** `ct.CanBeCanceled` is `false`; the initialization is bounded by the backend's own timeouts, since a store has no cancellation of its own

### Requirement: A data operation made from inside initialization passes straight through

The system SHALL mark the call flow that is running a store instance's `InitCore` / `InitCoreAsync` with
a per-instance `AsyncLocal<bool>` (`_initializingHere`), and SHALL make `EnsureInitialized()` /
`EnsureInitializedAsync(ct)` return immediately on that flow — before the `_initialized` check and before
taking `lock (_initLock)` / awaiting `_initLock.WaitAsync(ct)` — so that a concrete store whose
initialization calls one of its own public data operations (to seed defaults) proceeds instead of
recursing or deadlocking (TASK-163, SH-M316). The mark is per call flow and per instance: other threads
and tasks still wait on the lock.

#### Scenario: Sync InitCore seeds data through a public CRUD method

- **Given** an `AbstractStore<T>` subclass whose `InitCore()` calls `this.Create(seedRow)` to seed defaults
- **When** the first data operation triggers `EnsureInitialized()`
- **Then** the nested `Create` calls `EnsureInitialized()`, sees `_initializingHere.Value == true` and returns at once, and `CreateCore(seedRow, …)` runs — `InitCore()` is invoked once, with no recursion; the mark is cleared in a `finally` after `InitCore()` returns or throws

#### Scenario: Async InitCoreAsync seeds data through a public CRUD method

- **Given** an `AbstractAsyncStore<T>` subclass whose `InitCoreAsync()` awaits `this.CreateAsync(seedRow)` to seed defaults
- **When** the first data operation awaits `EnsureInitializedAsync()`
- **Then** the nested `EnsureInitializedAsync` checks the token, sees `_initializingHere.Value == true` and returns without awaiting the semaphore the outer call holds, so `CreateCoreAsync` runs and no deadlock occurs

#### Scenario: Another caller still waits for the initialization

- **Given** task A running `InitCoreAsync` with `_initializingHere` set on its own flow
- **When** task B, on a different flow, awaits `CountAsync()` on the same store
- **Then** B's `_initializingHere.Value` is `false`, so B queues on `_initLock` and does not observe the store before A's initialization finishes

### Requirement: The initialization flag is read without synchronization

The system SHALL declare `_initialized` as a private, non-`volatile` `bool` in both `AbstractStore<T>`
and `AbstractAsyncStore<T>`, and SHALL read it once outside the lock as the fast path of the
double-checked lock (`if (_initialized && CanTrustRememberedInitialization) return;` before `lock (_initLock)` / before
`await _initLock.WaitAsync(ct)`). No `volatile`, `Interlocked`, `Thread.MemoryBarrier`, or
`Lazy<T>`/`LazyInitializer` is used.

#### Scenario: Fast path performs an unsynchronized read

- **Given** an initialized store
- **When** any data operation calls `EnsureInitialized()`
- **Then** the method returns after reading the `_initializingHere` async-local, the plain `_initialized` field and the virtual `CanTrustRememberedInitialization`, taking no lock — this is the intended performance property of the design

#### Scenario: The write is published from inside the critical section

- **Given** a thread completing `InitCore()`
- **When** it assigns `_initialized = CanRememberInitialization`
- **Then** the assignment happens inside `lock (_initLock)` (sync) or between `WaitAsync` and the `finally`'s `Release()` (async), but the *reading* side has no matching acquire barrier — correctness of the unsynchronized read rests on the runtime's memory model rather than on any explicit annotation in the code

### Requirement: Filter-based bulk operations initialize only through their delegated reads

The system SHALL implement the composed bulk operations — `Update(filter, PropertyUpdate<T>)`,
`Update(filter, Action<T>)`, `Delete(filter)`, `Read()`, and their async counterparts — without any
direct `EnsureInitialized` call, relying on the delegated `Read` / `ReadAsync` (and then the per-item
`Update` / `UpdateAsync` or the bulk `Delete` / `DeleteAsync`) to perform initialization.

**The filter guards run ahead of that delegated read, so a refused operation never initializes the
store.** Each of the six filter-based destructive wrappers calls `RequireFilter` (the filter is present)
and then `RequireBoundedFilter` (it does not cover every row) as its first two statements, before the
`Read` that would otherwise open the initialization gate. A store that has only ever been asked to perform
a refused delete therefore remains uninitialized, and `InitCore` has not run.

This ordering is deliberate and is the safe one: initialization can have side effects — schema-ensure,
index creation, file or collection creation — and performing them on behalf of an operation that is about
to be refused would be work done for a caller who achieved nothing.

#### Scenario: Delete by filter on a never-initialized store

- **Given** a never-initialized `AbstractBulkStore<T>` subclass
- **When** the caller invokes `Delete(x => x.Guid == someGuid)`
- **Then** initialization happens inside the `Read(filter, null, null, null)` call that `Delete(filter)` makes first, and the subsequent `Delete(items)` finds `_initialized` already `true`

#### Scenario: A refused filter leaves the store uninitialized

- **Given** a never-initialized `AbstractBulkStore<T>` subclass and an empty collection `empty`
- **When** the caller invokes `Delete(x => !empty.Contains(x.Field))`, or `Delete(null!)`
- **Then** the guard throws before the delegated `Read` is reached, `InitCore` has **not** run, and
  `_initialized` is still `false` — a later legitimate call still initializes normally

#### Scenario: PropertyUpdate by filter routes through the Action overload

- **Given** a never-initialized `AbstractAsyncBulkStore<T>` subclass
- **When** the caller awaits `UpdateAsync(filter, propertyUpdate, ct)`
- **Then** it forwards to `UpdateAsync(filter, entity => updates.ApplyTo(entity), ct)`, which awaits `ReadAsync(filter, null, null, null, ct)` — the operation that actually initializes — and then awaits the single-entity `UpdateAsync(item, ct: ct)` once per matched item

#### Scenario: The PropertyUpdate overload evaluates its filter's collection operand more than once

- **Given** a filter whose collection operand is a *method call* rather than a captured collection
- **When** `Update(filter, propertyUpdate)` is called
- **Then** the operand is evaluated three times, not once — the `PropertyUpdate` wrapper guards, then
  delegates to the `Action` overload which guards again, and the delegated `Read` evaluates it a third
  time. Both guards are kept deliberately, because each covers a distinct partial-override case, so
  callers put captured collections rather than method calls in a filter

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

### Requirement: Destroy is a non-virtual wrapper that re-arms initialization

The system SHALL implement `Destroy()` and `DestroyAsync(ct)` as non-virtual public wrappers around the
`protected abstract` `DestroyCore()` / `DestroyCoreAsync(ct)` seams, and SHALL clear `_initialized` in a
`finally` after the core call — whether it returned or threw — so that the next data operation runs
`InitCore` / `InitCoreAsync` again and re-provisions the backing storage (TASK-163, SH-M307). The wrapper
does not call `EnsureInitialized` / `EnsureInitializedAsync` and takes no init lock. A `DestroyCore`
override that calls back into the public `Destroy` / `DestroyAsync` on the same call flow is refused with
`InvalidOperationException` instead of recursing.

#### Scenario: Destroy then reuse re-initializes

- **Given** an initialized store whose `DestroyCore()` drops its backing table
- **When** the caller calls `Destroy()` and then `Create(data)`
- **Then** `_initialized` is `false` after `Destroy()`, so `EnsureInitialized()` takes the lock and re-runs `InitCore()`, which recreates the table before `CreateCore` runs

#### Scenario: A throwing DestroyCore still clears the flag

- **Given** an initialized store whose `DestroyCore()` throws
- **When** the caller calls `Destroy()`
- **Then** the exception propagates and `_initialized` is nevertheless `false`, so the next operation re-runs initialization rather than trusting storage in an unknown state

#### Scenario: Destroy on a never-initialized store

- **Given** a store on which no data operation has ever run
- **When** `Destroy()` (or `await DestroyAsync(ct)`) is called
- **Then** `DestroyCore()` / `DestroyCoreAsync(ct)` executes with the backend never having been set up, because the wrapper does not initialize first

#### Scenario: A DestroyCore override that calls the public Destroy is refused

- **Given** a concrete store whose `DestroyCore()` calls `Destroy()` (or whose `DestroyCoreAsync` awaits `DestroyAsync()`) — the pre-TASK-163 `base.Destroy()` shape
- **When** the caller calls `Destroy()`
- **Then** the nested call sees the per-instance `_destroyingHere` async-local set and throws `InvalidOperationException` naming the store type and directing the override to call `base.DestroyCore` instead

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
- **Then** it delegates (through `StoreSave.Save`) to `Create(entity, storeDelegate)`, which is the call that initializes; when `entity.Guid` has a non-empty value it delegates to `Update(entity, …)` instead and returns `entity.Guid.Value`, and if that update throws `EntityNotFoundException` naming that Guid it falls through to `Create(entity, …)` under the same Guid, with the store delegate wrapped to run at most once across both calls

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
- **Then** `_initLock` is still undisposed — the `DestroyAsync` wrapper only runs `DestroyCoreAsync` and clears `_initialized`, and the field is private and inaccessible to the subclass
