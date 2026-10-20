---
id: TASK-685
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-19
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# A retry after a unique violation cannot run inside a PostgreSQL transaction — the slug and default decorators

## Context

Spawned from [[TASK-680]]'s close review (2026-10-19). Measured there on live PostgreSQL 16: inside a caller's
`SqlUnitOfWork`, an INSERT the unique key refuses **aborts the transaction**, so the next statement fails with
`25P02: current transaction is aborted, commands ignored until end of transaction block` — a raw `PostgresException` in
place of the store's `StoreUniqueViolationException` (rule 9: the exception type is a contract callers select on). SQL
Server keeps the transaction and the retry succeeds inside it (measured).

TASK-680 fixed this for the translation upsert (`LocalizedTranslationRows`: a re-read that fails rethrows the original
violation, and a live test pins both providers' outcome). The same catch-then-query shape exists, **unmeasured**, in:

- `Birko.Data.Patterns/Decorators/AsyncSluggableStoreWrapper.cs` — `CreateAsync` catches `StoreUniqueViolationException`
  and calls `SlugHeldAsync`, then retries the create (and the sync twin, and the update path if it does the same);
- `Birko.Data.Patterns/Decorators/AsyncDefaultStoreWrapper.cs` around lines 98-101 (TASK-632's bounded retry).

Rule 35 is the frame: a statement the server refuses inside a transaction is a provider limit the framework must route
around. Birko.Data.SQL has no savepoints today.

## Acceptance criteria

- [x] Measure both decorators on live PostgreSQL inside `SqlUnitOfWork` (and SQL Server for contrast): what the caller gets
- [x] Inside an aborted transaction the caller gets the typed `StoreUniqueViolationException`, never a raw `25P02` —
      either by not querying after the refusal, or by a savepoint around the attempt (decide, write it down)
- [x] Live tests pin the outcome per provider, red-verified

## Out of scope

- The translation upsert — fixed by [[TASK-680]].

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Drafted 2026-10-20 (Plan agent). Decided by the user 2026-10-20:_
- _approach A, the probe-failure fallback;_
- _the sync twins converted from exception filters to the same explicit fallback;_
- _store-level savepoints spawned as [[TASK-694]]._

**AC 1 scope, as read here:** "both decorators" covers all six classes with the catch-then-query shape:
`AsyncSluggableStoreWrapper`, `AsyncSluggableBulkStoreWrapper`, `AsyncDefaultStoreWrapper`, and their three sync twins.

**Guess to measure first (rule 54):** the sync twins probe inside an exception filter, and .NET swallows an exception
thrown in a filter. So on PostgreSQL they may already surface the original typed violation, silently.

1. **Measure:** live tests inside `SqlUnitOfWork` with a racing writer on another connection (an `IStoreWrapper` racer),
   run against today's code. Record the per-provider table here.
2. **One producer:** `StoreException.UnretryableUniqueViolation(violation, readFailure)` in Birko.Data.Core.
   `LocalizedTranslationRows.Unretryable` points at it.
3. **Async:** every post-refusal probe (`SlugHeldAsync`, `AnySlugHeldAsync`, `AnyStoredAsync`, the default-promotion
   probe) is wrapped. A probe failure other than cancellation throws the typed violation with the read failure
   appended.
4. **Sync:** the exception filters become catch blocks calling the same producer.
5. **Remarks:** document the outcome per provider inside a caller's transaction. PostgreSQL gives a typed error,
   because the transaction is aborted. MySQL gives a typed error, because its stale snapshot reads the slug as free and
   the bounded retry rethrows. SQL Server retries in place.
6. **Tests and docs:**
   - Live tests (per provider; SQLite skipped, being single-writer): in a transaction a slug create, a default promotion
     and a slug update, each sync and async; outside a transaction the retry still works.
   - Unit tests: a throwing probe gives the typed violation with the read failure, and cancellation passes through.
   - Mutations red.
   - Specs (store-decorator-composition, unit-of-work-and-transactions), the Patterns docs and Recent Updates.

## Outcome

**Measured before the fix (2026-10-20, live PostgreSQL 16, MySQL 8.4, SQL Server 2022).** A racer on another connection
commits the conflicting row right after the decorator's first probe. Each case runs inside the caller's `SqlUnitOfWork`:

| Case | PostgreSQL | MySQL | SQL Server |
|---|---|---|---|
| async slug create / update, async default promotion | raw `PostgresException` 25P02 (**the bug**) | typed refusal: the REPEATABLE READ re-probe finds nothing | retried in place, committed |
| sync slug create, sync default promotion | typed refusal, **by accident**: the filter swallowed the 25P02 | typed refusal | retried in place |
| outside a transaction | retried | retried | retried |

The guess (rule 54) held: the sync twins were typed only because .NET swallows an exception thrown in a filter.

**After.** Every post-refusal probe goes through `StoreException.ProbeAfterRefusal(Async)` (one producer:
`UnretryableUniqueViolation`, which `LocalizedTranslationRows.Unretryable` now delegates to). On PostgreSQL every case,
sync and async, throws `StoreUniqueViolationException`. Its message names the failed re-check (`25P02`), and its inner
exception is the original refusal. MySQL and SQL Server are unchanged. Cancellation of the probe passes through.

**Tests.**
- `Task685InTransactionLiveTests`: 6 per provider (PostgreSQL, MySQL, SQL Server). Knobs: `RetriesInsideATransaction`
  (SQL Server only) and `ProbeFailsAfterRefusal` (PostgreSQL only; asserts the message and inner exception). The suite
  has its own tables, `T685Slugs` / `T685Defaults`. Sharing TASK-632's tables failed both suites when they ran in
  parallel, because each drops them (rule 61).
- `Task632RetryTests`: +10 (every sync and async decorator overload with a failing probe; cancellation; the async helper; rethrow as-is).

**Mutations, all red:**

| Mutation | Red |
|---|---|
| sync slug fallback made silent | 2 unit + PostgreSQL sync slug live |
| sync batch fallback silent | 1 unit |
| sync default fallback silent | 1 unit + PostgreSQL sync default live |
| async slug fallback removed | PostgreSQL async create and update live |
| async default fallback removed | PostgreSQL async default live |
| cancellation wrapped too | 2 unit |
| read failure not named in the message | 4 unit + 5 PostgreSQL live |
| retry disabled | 7 live (SQL Server in place, and outside a transaction) |

**Close review (7 findings):**
- Fixed:
  - Unit tests now drive every async decorator overload and the sync updates / default batches (+4). They were sync-only
    before. Batch items carry Guids, so the stored-items probe really queries.
  - Per-overload mutations are red, all 9 but one equivalent mutant: silencing the sync default batch's first probe still
    falls through to the second, wrapped probe.
  - docs/patterns.md scoped "every provider retries" to the four SQL providers.
- Recorded (behaviour change outside transactions):
  - A sync probe failure used to be swallowed by the filter and surface as the bare refusal. Now it is the wrapped
    refusal, or an `OperationCanceledException` passes through.
  - The sync probe now runs after the inner store has unwound, as the async one always did.
- Accepted:
  - A connection fault during the re-check reads as a unique refusal, and its message is kept in the text. No type
    selects on the old exception (rule 9).
  - `UnretryableUniqueViolation` returns or throws, as `LocalizedTranslationRows.Unretryable` did.
  - The live suite drops only its own T685 tables, one server per provider class, in `Dispose`.

**Regression:**
- Patterns 158/158, Data 235/235, Composition 26/26 (every suite that uses the slug or default wrappers).
- Migrations.SQL 244/244 live, Localization 295/295, Localization.SQL 120/120 live.
- The solution builds with no new warnings.
