# Recovered findings — the three unrated areas

Recovered 2026-07-31 from the first harvest workflow's journal
(`wf_0987dab2-cb0/journal.jsonl`, run 2026-07-30). **Not** re-derived — this is the original agent output,
verbatim apart from repairing mangled em-dashes.

Counts match the coverage note exactly: `core-model-contracts` **4**, `store-lazy-initialization` **6**,
`unit-of-work-and-transactions` **6** = **16**.

Severities in the tables below are a **proposal**, not the harvester's — the first pass emitted no severity
field at all (that is why these were lost; see [`STORY.md`](STORY.md)). They need confirming before these
fold into the severity stories.

---

## area: core-model-contracts (4)

### CMC-1 — `AbstractLogModel.CopyTo` is an overload, not an override: timestamps silently dropped via a base-typed reference

`../Birko.Data.Core/Models/AbstractLogModel.cs:13` · proposed **medium**

Base declares `virtual AbstractModel CopyTo(AbstractModel?)`; derived declares
`AbstractLogModel CopyTo(AbstractLogModel?)` — different parameter type, so it neither overrides nor hides.
**Verified by compiling the hierarchy:** via an `AbstractModel`-typed or `ICopyable<AbstractModel>`
reference, `target.CreatedAt`/`UpdatedAt` stay `0001-01-01`, while the same call through an
`AbstractLogModel` reference transfers 2020/2021. Infrastructure holding models as `AbstractModel` loses
audit timestamps with no error.

### CMC-2 — `AbstractLogModel.LoadFrom` is an overload, not an override: `ILoadable<IGuidEntity>` dispatch discards timestamps the argument carries

`../Birko.Data.Core/Models/AbstractLogModel.cs:26` · proposed **medium**

`AbstractModel.LoadFrom(IGuidEntity)` is virtual and never overridden. **Verified:**
`ILoadable<IGuidEntity> il = target; il.LoadFrom(logEntitySource)` sets `Guid` but leaves `CreatedAt` at
`0001-01-01` though the runtime argument is an `ILogEntity` carrying `2020-01-01`. Worse than the `CopyTo`
case: the data is demonstrably present on the argument and the selected method has no code to read it.
`AbstractLogModel` thus exposes two `ILoadable<T>` contracts of differing fidelity.

### CMC-3 — `CopyTo` with a null/omitted target returns `this` instead of allocating, so the "clone" aliases the source

`../Birko.Data.Core/Models/AbstractModel.cs:15` · proposed **medium**

Both `CopyTo` bodies open with `if (clone == null) { return this; }`, but the XML doc on
`Birko.Data.Core/Models/ICopyable.cs` says implementations "allocate a fresh instance". **Verified:**
`ReferenceEquals(src.CopyTo(), src)` is true, and assigning `UpdatedAt` on the returned "copy" changes
`src.UpdatedAt`. Callers trusting the doc get shared mutable state. `AbstractModel` is abstract so it cannot
self-allocate; honest options are to throw, or to drop the default plus the doc claim.

### CMC-4 — `LoadFrom` parameter is null-tolerant at runtime but not annotated nullable, forcing CS8625 on callers

`../Birko.Data.Core/Models/AbstractModel.cs:21` · proposed **low**

`public virtual void LoadFrom(IGuidEntity data)` guards with `if (data != null)`, so null is an accepted,
meaningful input, yet the parameter is non-nullable. A caller passing null gets a nullable diagnostic even
though the callee handles it — friction against the project's no-nullable-warnings rule. Same shape on
`AbstractLogModel.LoadFrom(ILogEntity data)`. Either annotate `IGuidEntity?` or drop the guard and throw.

---

## area: store-lazy-initialization (6)

### SLI-1 — `Destroy()`/`DestroyAsync()` cannot reset `_initialized`, so a destroyed store never re-initializes

`../Birko.Data.Stores/AbstractStore.cs:48` · proposed **medium**

`_initialized` is private (`AbstractStore.cs:15`, `AbstractAsyncStore.cs:17`) with no protected accessor or
reset hook, and `Destroy`/`DestroyAsync` are bare `public abstract` with no base wrapper. A store whose
`Destroy()` drops its table stays flagged initialized, so the next `Create()` skips `InitCore()` and runs
`CreateCore` against a destroyed backend. Destroy-then-reuse (test fixtures, teardown/rebuild flows)
silently hits missing tables instead of recreating them.

### SLI-2 — `InitCore` recurses to StackOverflow if it uses a public CRUD method (sync only)

`../Birko.Data.Stores/AbstractStore.cs:27` · proposed **medium**

`lock (_initLock)` is a re-entrant Monitor and `_initialized = true` is set only after `InitCore()` returns
(line 31). An `InitCore()` seeding defaults via `this.Create(…)` re-enters `EnsureInitialized()` on the same
thread, still sees false, and calls `InitCore()` again — unbounded recursion ending in an uncatchable
`StackOverflowException`. An "initializing" latch or an explicit throw would make this diagnosable.

### SLI-3 — `InitCoreAsync` deadlocks the store if it awaits a public CRUD method (async only)

`../Birko.Data.Stores/AbstractAsyncStore.cs:39` · proposed **medium**

`SemaphoreSlim` has no owner affinity, so a nested `EnsureInitializedAsync` from inside `InitCoreAsync`
awaits `_initLock.WaitAsync(ct)` on a semaphore the outer call holds and never completes. The store hangs
permanently unless the token is cancelled — and **it fails differently from the sync sibling for identical
subclass code**, so the mistake ports between hierarchies undetected.

### SLI-4 — `_initialized` is a non-volatile bool read outside the lock in both hierarchies

`../Birko.Data.Stores/AbstractStore.cs:15` · proposed **low**

Classic double-checked-locking hazard: the fast-path read (`AbstractStore.cs:26`,
`AbstractAsyncStore.cs:38`) has no acquire barrier — no `volatile`, `Interlocked`, `MemoryBarrier`,
`Lazy<T>` or `LazyInitializer` anywhere. Under ECMA-335 a reader may see `true` before `InitCore()`'s side
effects are visible, or the JIT may cache the field across a loop. Correctness rests on the stronger
de-facto CLR/x86 model, not on the code; `volatile` costs nothing here.

### SLI-5 — One-time async init runs under whichever concurrent caller's token wins the lock race

`../Birko.Data.Stores/AbstractAsyncStore.cs:43` · proposed **medium**

`InitCoreAsync(ct)` gets the token of whoever acquired the lock; it is neither replaced with
`CancellationToken.None` nor linked with queued callers' tokens. A caller with a 50 ms timeout arriving
first aborts the shared initialization mid-flight (possibly leaving the backend half-built), and a queued
caller with `None` then repeats the whole init. Which token governs one-time setup is decided by arrival
order.

### SLI-6 — `SemaphoreSlim _initLock` is never disposed; no `IDisposable` on the async bases

`../Birko.Data.Stores/AbstractAsyncStore.cs:18` · proposed **low**

A `SemaphoreSlim` is allocated per instance, yet neither `AbstractAsyncStore<T>` nor
`AbstractAsyncBulkStore<T>` implements `IDisposable`/`IAsyncDisposable`, and `DestroyAsync` is abstract with
no base behaviour. Per-request store instantiation leaves undisposed semaphores for the GC. Low severity
(the wait handle is lazily allocated and unused), but it is an unowned disposable on a base every async
store inherits.

---

## area: unit-of-work-and-transactions (6)

### UOW-1 — ES commit failure leaves the buffer queued and the UoW active, so a retry double-applies succeeded items

`../Birko.Data.ElasticSearch/UnitOfWork/ElasticSearchUnitOfWork.cs:140` · proposed **high**

`_context = null` sits after both throw sites (`IsValid` at 125, `Errors` at 132). On a partial failure the
UoW stays `IsActive` with every operation still buffered, so a second `CommitAsync` **re-sends the items
that already succeeded**. There is no way to trim the successful subset, leaving only `RollbackAsync`, which
silently abandons the failed ones. Needs cleanup in a `finally`, or per-item outcomes exposed for a targeted
retry.

### UOW-2 — `SqlUnitOfWork` leaks the `DbConnection` when `OpenAsync` or `BeginTransactionAsync` throws

`../Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs:59` · proposed **medium**

The field is assigned before `OpenAsync`/`BeginTransactionAsync`. If either throws, `IsActive` stays false
(`Commit`/`Rollback` then throw `NoActiveTransactionException`) and the connection is released only by an
eventual `Dispose`. Worse, a retried `BeginAsync` passes the `IsActive` guard and overwrites `_connection`
without closing the previous one, so **repeated failed Begins leak a pooled connection each**. Needs
try/catch cleanup around lines 59–63.

### UOW-3 — A failed SQL `CommitAsync`/`RollbackAsync` skips `CleanupAsync`, holding the connection open

`../Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs:72` · proposed **medium**

Cleanup is a sequential statement, not a `finally`. A deadlock-victim commit, dropped connection or
cancellation propagates and `CleanupAsync` never runs, so `IsActive` remains true and `Context` non-null.
Callers using the obvious try-Commit/catch-Rollback pattern then hit a second failure on the already-broken
transaction. `RollbackAsync` at line 82 has the same shape.

### UOW-4 — `SqlUnitOfWork` is declared in the global namespace

`../Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs:17` · proposed **low**

The file has usings and a doc comment but **no `namespace` declaration**, so the type lands in the global
namespace, unlike `SqlTransactionContext` (`Birko.Data.SQL.Stores`) and `ElasticSearchUnitOfWork`
(`Birko.Data.ElasticSearch.UnitOfWork`). Consumers cannot reach it via a using, and it pollutes every
compilation unit referencing `Birko.Data.SQL`. Almost certainly an omission.

### UOW-5 — `SqlTransactionContext` constructor performs no null validation

`../Birko.Data.SQL/Stores/SqlTransactionContext.cs:15` · proposed **low**

Both arguments are assigned directly, while every sibling guards its constructor (`SqlUnitOfWork` throws
`ArgumentNullException` for connector and settings, `ElasticSearchUnitOfWork` for client). A context with a
null `Connection` or `Transaction` is handed to enlisted stores via `SetTransactionContext` and fails with a
`NullReferenceException` at the first CRUD call, far from the cause.

### UOW-6 — `response.OriginalException!` is null-suppressed but can be null on a ServerError-only response

`../Birko.Data.ElasticSearch/UnitOfWork/ElasticSearchUnitOfWork.cs:129` · proposed **low**

Line 128 explicitly handles `OriginalException` being null (`?.Message`), then line 129 passes
`response.OriginalException!` as the inner exception. Passing null is legal at runtime so it does not crash,
but the `!` asserts an invariant the adjacent line contradicts — misleading readers and any future
nullable-flow refactor about whether `InnerException` can be null.

---

## Outcome — rated, ID'd and folded on 2026-09-08 by [[TASK-195]]

**All 16 were re-verified against the code first.** Every one still describes the shipped code — none had
been fixed or gone stale in the 39 days since recovery. Then five turned out to be duplicates and one
proposed severity did not survive measurement, so **11** findings were folded in, not 16.

### Final mapping

| Recovered | Verdict | Outcome | Owner |
|---|---|---|---|
| `CMC-1` | CONFIRMED-NARROWER | `SH-M422` | [[TASK-323]] |
| `CMC-2` | CONFIRMED-NARROWER | `SH-M423` | [[TASK-323]] |
| `CMC-3` | CONFIRMED | `SH-M424` | [[TASK-323]] |
| `CMC-4` | CONFIRMED | `SH-L388` | [[TASK-326]] |
| `SLI-1` | CONFIRMED | **duplicate of `SH-M307`** | [[TASK-163]] |
| `SLI-2` | CONFIRMED | **duplicate of `SH-M316`** (sync half) | [[TASK-163]] |
| `SLI-3` | CONFIRMED | **duplicate of `SH-M316`** (async half) | [[TASK-163]] |
| `SLI-4` | CONFIRMED | **duplicate of `SH-L297`** | [[TASK-182]] |
| `SLI-5` | CONFIRMED | `SH-M425` | [[TASK-324]] |
| `SLI-6` | CONFIRMED | **duplicate of `SH-L298`** | [[TASK-182]] |
| `UOW-1` | CONFIRMED-NARROWER, **downgraded high → medium** | `SH-M426` | [[TASK-325]] |
| `UOW-2` | CONFIRMED | `SH-M427` | [[TASK-325]] |
| `UOW-3` | CONFIRMED | `SH-M428` | [[TASK-325]] |
| `UOW-4` | CONFIRMED | `SH-L389` | [[TASK-327]] |
| `UOW-5` | CONFIRMED | `SH-L390` | [[TASK-327]] |
| `UOW-6` | CONFIRMED | `SH-L391` | [[TASK-327]] |

### Confirmed severity split — **0 high**, not 1

| Severity | Count | Items |
|---|---|---|
| high | **0** | — |
| medium | 7 | CMC-1, CMC-2, CMC-3, SLI-5, UOW-1, UOW-2, UOW-3 |
| low | 4 | CMC-4, UOW-4, UOW-5, UOW-6 |
| duplicates | 5 | SLI-1, SLI-2, SLI-3, SLI-4, SLI-6 |
| **total** | **16** | |

Totals become **57 high · 428 medium · 391 low = 876**, replacing 865.

### ⚠ Where the proposal above was wrong, and why it matters

- **It predicted 881 and the answer is 876.** The proposal counted `SLI-4` and `SLI-6` even though it knew
  they were duplicates (the over-count [[TASK-195]] anticipated), *and* it did not know that `SLI-1`,
  `SLI-2` and `SLI-3` were duplicates too. Those three were found only by checking every recovered finding
  against the areas whose globs overlap `Birko.Data.Stores` — the check TASK-195 required before any id was
  minted, and it changed the count by 3.
- **`UOW-1` does not clear the high bar.** Two measurements decided it: every operation
  `BulkOperationContext` can buffer is **idempotent** (`Index` full-document, `Delete` by id, `Update` with
  `Doc(partialDocument)` — no script, no `create`), so the *"a retry double-applies succeeded items"*
  consequence overwrites with the same value; and the class's own doc comment already states *"This is NOT
  a true ACID transaction. Individual operations within the bulk may succeed or fail independently"*, so
  partial application is documented rather than silent — and the caller gets an exception. What remains is
  real (stale `IsActive`, an intact buffer, no per-item outcomes for a targeted retry) but is medium.
- **So the recovered set contains no high finding**, and [[STORY-051]]'s count and its 15-task
  decomposition are unaffected. No id inside any existing range was renumbered, moved or reused — the
  constraint that protects the explicit `findings:` lists on 44+ task files.
- **`unit-of-work-and-transactions` and `core-model-contracts` had no overlap at all**: measured, the
  findings doc contained **0** references to `AbstractModel.cs`, `AbstractLogModel.cs`, `SqlUnitOfWork.cs`,
  `SqlTransactionContext.cs` or `ElasticSearchUnitOfWork.cs` before this fold. The duplication is confined
  to the two store bases.
