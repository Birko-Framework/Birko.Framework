---
id: TASK-694
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-685]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Savepoint-scoped writes inside a caller's transaction, so PostgreSQL can retry after a refusal

## Context

Spawned from [[TASK-685]] at plan time, 2026-10-20 (decided by the user). Inside a caller's transaction, a statement
PostgreSQL refuses (a unique violation) aborts the whole transaction. Every later statement then fails with `25P02`, so
nothing can re-read and retry there. TASK-685 makes the decorators surface the typed `StoreUniqueViolationException`
instead of the raw error. TASK-680 and [[TASK-683]] (`LocalizedTranslationRows`, `SqlLocalizedWriteBoundary`) route
around it the same way, or re-run the whole write in a new transaction when they own it.

A savepoint around each write inside a joined or explicit transaction would keep PostgreSQL's transaction usable:
`SAVEPOINT`, then `ROLLBACK TO` on refusal. Under READ COMMITTED the re-check after the rollback sees the other writer's
commit, so the in-place retry would work. The syntax differs: SQL Server uses `SAVE TRANSACTION`, while SQLite and MySQL
use `SAVEPOINT`.

It does not help MySQL. Its transaction survives a refusal anyway, but the re-check reads the REPEATABLE READ snapshot
from before the other writer committed (measured in TASK-684).

Bigger than TASK-685, because it touches every SQL write path. It costs two extra round trips per write inside a
transaction. It also changes behaviour: a PostgreSQL caller could catch a violation and carry on in its transaction.
Rule 35 frames it as a provider limit routed around in one place.

## Acceptance criteria

- [x] Decide the scope: PostgreSQL only, or every provider through a connector's savepoint syntax. Decide whether
      savepoints are always on inside a transaction or opt-in. Measure the round-trip cost before deciding (rule 10:
      priced on the steady state).
- [x] One producer in Birko.Data.SQL: a write inside a joined or explicit transaction rolls back to its savepoint on a
      unique violation, before the violation is translated.
- [x] Live tests on PostgreSQL: the slug / default decorators and the translation upsert retry in place inside a
      caller's `SqlUnitOfWork`. TASK-685's and TASK-683's per-provider knobs are updated, and the tests that pinned
      "typed refusal" are inverted (rule 56).

## Out of scope

- MySQL's stale snapshot: a savepoint does not change what a REPEATABLE READ re-check sees.

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Planned 2026-10-20 after measuring. Decided by the user: savepoints only around the writes a caller retries, and
only where the provider aborts its transaction on a refused statement (PostgreSQL). This changes AC 2 from "every
write inside a transaction" to "every retried write" (rule 10)._

**Measured cost (2026-10-20, local Docker, 1 000 single-row inserts inside a transaction, best of 3).** `DbTransaction.Save`
+ `Release` around each write:

| Provider | Plain | With a savepoint | Extra |
|---|---|---|---|
| PostgreSQL 16 | 1.10 ms/write | 2.13 ms/write | +93% |
| SQL Server 2022 | 1.48 ms/write | 2.86 ms/write | +94% |
| MySQL 8.4 | 1.25 ms/write | 3.50 ms/write | +180% |
| SQLite | 0.007 ms/write | 0.019 ms/write | negligible |

After a refusal and `ROLLBACK TO`, the transaction answered again on all four. It only helps on PostgreSQL: SQL
Server already retries in place, and MySQL's REPEATABLE READ re-check is not changed by a savepoint (TASK-684).
Every PostgreSQL write in a transaction paying 2× for a retry only a few writes have was priced out.

1. `Birko.Data.Stores`: `IRetryableWriteStore` (`RunRetryable` / `RunRetryableAsync`) and `RetryableWrite`, which
   finds it beneath decorators through `IStoreWrapper` or runs the write as is.
2. `Birko.Data.SQL`: `AbstractConnectorBase.AbortsTransactionOnError` (false; PostgreSQL true, inherited by
   TimescaleDB). `SqlRetryableWrite` is the one producer the two base stores delegate to. It takes a savepoint only
   when the flag is set and a transaction is active (the store's context or the ambient boundary), rolls back to it on
   any failure, and releases it on success. It uses ADO.NET's `Save` / `Rollback(name)` / `Release`, so no dialect.
3. The slug and default decorators (sync and async, single and batch) and both `SaveMany` paths of the translation
   upsert wrap exactly the write they retry.
4. Tests:
   - Invert TASK-685's and TASK-683's PostgreSQL knobs (rule 56).
   - Live tests of the producer: a refused retryable write leaves the transaction usable and commits the earlier
     write; an ordinary write still aborts where the provider does.
   - Unit tests of the helper, and that every decorator routes each attempt through the capability.
   - Mutations.

## Outcome

Done 2026-10-20, as planned. The decision (savepoints only around retried writes, only where a refusal aborts the
transaction) is recorded above, with the cost measurement that priced the alternative out.

**Measured after, live (PostgreSQL 16, MySQL 8.4, SQL Server 2022):**
- PostgreSQL now retries in place inside a caller's `SqlUnitOfWork`: the slug create / update and the default
  promotion, sync and async, and the translation upsert.
- A refused retryable write leaves the PostgreSQL transaction answering, and the commit keeps the earlier write. The
  same write made directly still aborts it: an ordinary write takes no savepoint.
- Outside a transaction a retryable write takes none. MySQL still refuses (snapshot), and SQL Server is unchanged.
- TASK-685's and TASK-680/683's PostgreSQL knobs are inverted (rule 56). TASK-685's `ProbeFailsAfterRefusal` knob is
  removed: false for every provider now, so its branch was dead. The probe-failure fallback stays covered by unit tests.

**Close review (7 findings, all handled):**
- **Gating.** The default decorator took a savepoint for non-default writes, and every decorator for its last attempt,
  though neither is retried. Each wrap now passes the store only while the write can be retried
  (`RetryableWrite.Run(null, …)` runs it as is). The translation retry's second create is unwrapped.
- **Walker.** `RetryableWrite.Find` uses `StoreChain.Walk`, the one chain walker (rule 16).
- **Cancellation.** A savepoint is released without the caller's token, so a cancellation after an applied write
  cannot report it as failed.
- **Rollback failure.** A failed `ROLLBACK TO` is recorded on the write's exception (`Data["Birko.SavepointRollbackFailure"]`)
  and no longer dropped (rule 26).
- **XML docs.** The docs my insertions had displaced are back on their members.
- **Boundary docs.** `SqlLocalizedWriteBoundary`'s remarks are corrected.
- **Spawned [[TASK-697]].** A joined localized boundary marks the caller's unit rollback-only on a refusal that a
  decorator above it then retries. This already happened on SQL Server and now reaches PostgreSQL; unmeasured.

**Tests:**
- `RetryableWriteTests` (5): found beneath decorators, runs as is without the capability, every overload, the write's
  own exception, a wrapper that returns itself.
- `Task632RetryTests` +3:
  - each attempt routes through the capability, sync and async;
  - a non-default write and the last attempt take no savepoint (5 refusals → 4 savepoints).
- `Task685InTransactionLiveTests` +4 per provider:
  - the producer, async and sync;
  - an ordinary write;
  - outside a transaction.

**Mutations, all red (11):**
- PostgreSQL flag off (7 + 1 live);
- no rollback to the savepoint (async 4 + 1, sync 2);
- the ambient transaction not found (6 + 1);
- the wrappers not walked (2 unit + 5 + 1 live);
- an async slug create, a sync default promotion or the translation create not wrapped;
- the three gates removed.

**Regression:**
- All 33 suites before the review fixes: every importer of Birko.Models.SQL, plus Patterns, Data, Composition and
  Localization, live where gated.
- After the fixes: Patterns 161/161, Data 240/240, Migrations.SQL 256/256 live, Localization.SQL 120/120 live,
  Localization 295/295.
- The solution builds with no new warnings.
