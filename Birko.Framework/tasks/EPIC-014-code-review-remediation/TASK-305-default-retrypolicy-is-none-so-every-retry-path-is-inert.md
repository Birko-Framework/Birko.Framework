---
id: TASK-305
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
related: [TASK-258, TASK-291, TASK-294, TASK-270, TASK-296]
findings: [CR-M144]
pr: null
github-issue: null
affects: [Birko.Data.SQL]
---

# The default `RetryPolicy` is `None`, so every retry path in the SQL layer is inert — decide whether it should be

## Context — the decision three closed tasks each declined to make

`AbstractConnectorBase.RetryPolicy` is declared

```csharp
public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.None;   // AbstractConnectorBase.cs:79
```

and `ExecuteWithRetry[Async]` short-circuits to a bare call when `MaxRetries <= 0`. So as shipped, **no
statement in the SQL layer is ever retried, on any provider.**

Three tasks have now met this and each correctly ruled it out of their own scope:

- **[[TASK-258]]** asked what `retryWhenOwned`'s documented *"preserve each provider's retry policy"*
  actually preserves. The answer: **a no-op.** `retryWhenOwned: true` and `false` are behaviourally
  identical everywhere, SQLite included, so **CR-M144's documented SQLite bulk retry does not happen** and
  that task's criteria 1 and 2 became unanswerable rather than unmeasured.
- **[[TASK-291]] / [[TASK-294]]** found that a blanket exception rewrap had been defeating
  `IsTransientException`, so *"a `RetryPolicy` a consumer had configured silently never fired"*. They fixed
  the rewrap and recorded: *"No retry policy was added. `RetryPolicy.None` is still the default; what
  changed is that a consumer who sets one now gets it."*

Per § Task tracking — *an `## Out of scope` bullet that describes WORK gets an id* — this is that id. Filed
as a decision, not an implementation: it is a resilience-policy question with a blast radius across every
provider, and nothing measured so far argues for a particular answer.

## Re-measured 2026-09-08, and the claim is wider than TASK-258 recorded

TASK-258's sweep was 2026-08-22 and ~30 tasks old, so it was re-run rather than cited:

| where | `RetryPolicy =` assignments |
|---|---|
| framework production code (all `Birko.*`) | **0** |
| all 16 consumer repos — production **and** test | **0** |
| any `appsettings*.json` in any consumer | **0** |
| framework test code | **3 files** — `RetryTests.cs`, `RetryWhenOwnedTests.cs`, `RewrapClassificationTests.cs` |

The third is new since TASK-258's count (it arrived with TASK-291/294), which is the only correction: the
conclusion is unchanged and the surface is one file larger. **Nothing outside a test has ever configured a
retry**, so whatever is decided here cannot regress a consumer's configured behaviour — there is none.

## ⚠ A measured, reproduced case where the machinery would have helped and did not (2026-09-08)

Found while working [[TASK-276]] — not sought, which is why it is worth recording. Running the
`Birko.Data.SQL.MSSql.Tests` suite 12 times **under CPU load** against live SQL Server 2022
(16.0.4265.3) produced one hard failure:

```
NullableUniqueColumnLiveTests.A_required_unique_column_keeps_its_inline_constraint
Microsoft.Data.SqlClient.SqlException : Transaction (Process ID 72) was deadlocked on lock
    resources with another process and has been chosen as the deadlock victim. Rerun the transaction.
```

0 failures in 12 idle runs of the same binary, so load is the lever (TASK-276's own finding, confirmed
here on a second suite).

**The classification is already correct — only the policy is missing.**
`MSSqlConnector.IsTransientException` explicitly enumerates `1205 // Deadlock victim`, and its summary
advertises *"Detects SQL Server transient errors: deadlocks (1205), timeouts (-2), …"*. So the framework
identifies the textbook retryable error, the server itself says **"Rerun the transaction"**, and nothing
retries it — because `RetryPolicy.None` short-circuits `ExecuteWithRetry` before the classification is
ever consulted.

That matters to this decision in three ways:

- It is evidence for a **non-zero default** that TASK-258's file did not have. The argument there was
  structural (*a flag that preserves a no-op*); this is an observed failure that a single retry would
  have absorbed, on the provider whose own driver documentation treats deadlock retry as the caller's
  job.
- ⚠ **It does not settle it, and must not be read as doing so.** One deadlock in 12 loaded runs of a
  test suite is not a production measurement, the contending statements were *DDL from parallel test
  classes against one database* (an unusual shape), and the safety of retrying depends on the failed
  attempt having left nothing behind — which for a deadlock victim SQL Server guarantees by rolling it
  back, but which is a per-provider, per-path claim (see the constraint below).
- It bears on **option 3**: SQL Server has a rich transient set already enumerated and a server that
  asks to be retried; SQLite's case is now weak (TASK-296's WAL). That asymmetry is an argument for a
  per-provider default rather than one number for all four.

## The decision to make

1. **Leave `None` and document it.** The retry machinery becomes an explicitly opt-in feature; CR-M144's
   claim about SQLite is corrected to "available, not default" wherever it is written down. Costs nothing
   and keeps every current measurement valid. ⚠ But it leaves a framework that ships a resilience
   mechanism nothing turns on, which is how this went unnoticed for as long as it has.
2. **Ship a conservative non-zero default** (e.g. `RetryPolicy.Default`, on transient classifications
   only). ⚠ This is the option with real blast radius: it changes timing and failure surfacing on every
   provider at once, and a retry is only safe where the failed attempt left nothing behind — the property
   CR-M144 argued for SQLite's bulk path *because the whole batch rolls back first*, which is a
   per-provider claim, not a general one.
3. **Default per provider**, where each connector states its own policy the way
   `SupportsTransactionalDdl` / `FoldsUnquotedIdentifiers` / `RequiresOrderByForPaging` already state
   per-provider capability. Fits the framework's existing shape for exactly this kind of divergence, and is
   the only option under which `retryWhenOwned`'s per-provider structure means anything.

## Constraints any answer has to respect

- ⚠ **A policy cannot be scoped per settings object as things stand.** [[TASK-270]] measured that
  `Settings.GetId()` is `Location:Name(:UserName:Port)` and carries **neither** `RetryPolicy` nor
  `CommandTimeout`, while `DataBase.GetConnector` caches per (type, settings id) — so two settings objects
  differing only in policy already share one connector and the **first** caller's value silently wins for
  everyone. Any "let the consumer configure it" answer inherits that, and `RetryPolicy` is the sole entry
  in that task's connector-shared-state ledger for this reason.
- **The participating path must keep never retrying**, whatever is decided. Re-running statements inside a
  transaction whose earlier ones already succeeded can only fail differently, and retrying is the boundary
  owner's decision (TASK-242). `RetryWhenOwnedTests` pins this structurally — the participating branch
  returns before the flag is read — so it survives a policy landing.
- ⚠ **Two tests are the alarm and must be answered, not updated.**
  `RetryWhenOwnedTests.WithTheSHIPPEDDefaults_RetryWhenOwnedTrue_DoesNotRetry_BecauseNoRetryPolicyIsEverConfigured`
  and `…_TheFlagMakesNoDifferenceEitherWay_AsyncHalf` exist to **fail** the day a default lands. That is
  them working. Whoever changes the default owes TASK-258's criteria 1 and 2 a real measurement at that
  moment — see below.
- **The motivating contention is much rarer than it was.** [[TASK-296]] put SQLite on WAL, where readers do
  not block writers, so the `SQLITE_BUSY` storms that made CR-M144's retry look valuable are largely gone.
  Re-measure the transient rate before pricing option 2 or 3 against it.

## Decision — 2026-11-04, by the user: a small default retry

**Approved: `AbstractConnectorBase.RetryPolicy` defaults to a small retry with backoff, not `None`.** It applies only
to failures `IsTransientException` already classifies as transient (deadlock victim, timeout, dropped connection), so a
constraint violation or a syntax error is never retried. A consumer can still set `RetryPolicy.None`. Chosen over
"keep `None` and document it" and over "retry deadlock victims only": the 2026-09-08 SQL Server deadlock above is the
case it exists for, and no consumer has ever configured a policy, so the change cannot override a consumer's choice.

This task is now the implementation. The retry count and backoff are implementation details, set within the
constraints below and recorded in the Outcome. Every retry path that was inert (TASK-258's `retryWhenOwned`, CR-M144's
SQLite bulk retry) has to be re-checked, because it now does something.

## Acceptance criteria

- [x] A decision recorded on the three options, with the measurement behind it — not a preference.
- [x] Whatever is chosen, **CR-M144's claim is made true or corrected.** It currently reads as though
      SQLite retries `SQLITE_BUSY`/`SQLITE_LOCKED` on the bulk path; that is false as shipped.
- [x] If a non-zero default lands, **[[TASK-258]]'s criteria 1 and 2 are answered live in this task**,
      since they only become answerable here:
      - SQLite retries **and converges** — assert the retry happened *and* the resulting row set is
        correct, with no duplicate or partially-applied rows. "Did not throw" is not the assertion.
      - The three server providers make **exactly one attempt** on their own-connection bulk path, so no
        consumer silently gains or loses a retry.
- [x] (N/A: a default landed.) If `None` is kept, the two alarm tests stay green and the reason is written where a reader of
      `RetryPolicy` will meet it.
- [x] Proven able to fail.

## Outcome — 2026-11-05

**Narrowed with the user before building (2026-11-05).** The decision listed "deadlock victim, timeout, dropped
connection". A timeout or a connection lost mid-statement can arrive *after* the server applied the INSERT or the
COMMIT, so retrying it can write twice. The user chose: retry only what the server guarantees it did not apply, plus
anything that failed before a statement was sent. The motivating 2026-09-08 deadlock is in that set.

**Built** (rule 66 records it):

- `AbstractConnectorBase.RetryPolicy` defaults to `DefaultRetryPolicy()`: 2 retries, 50 ms then 100 ms, exponential,
  ±25% jitter, at most 500 ms. A new instance per connector (rule 36). `RetryPolicy.None` still turns it off.
- New `IsNotAppliedException` beside `IsTransientException`, narrower than it. Base: nothing. SQL Server 1205;
  PostgreSQL 40001, 40P01; MySQL 1213, 1205; SQLite 5, 6.
- `ExecuteWithRetry[Async]` hands each attempt a `RetryAttempt`; the wrapped paths (`RunCommand`, `RunCommandTransaction`,
  `RunBulk` owned, sync and async) call `MarkSent()` once the connection is open and the transaction begun. Retried: a
  transient failure before that, or an `IsNotAppliedException` after it. The participating path is unchanged: it never
  retries (TASK-242). The server providers' bulk sites still pass `retryWhenOwned: false`.
- **Found while answering TASK-258's criterion 1:** a *store's* bulk write never reached the connector's retried bulk
  path. `Atomically` (TASK-629) opens the store's own transaction and the connector's bulk write participates in it. So
  SQLite busy — reported by Microsoft.Data.Sqlite at `BEGIN IMMEDIATE` — failed every store bulk write, whatever the
  connector did (measured: the new `BusyRetryTests` store cases were red with the default policy in place). `Atomically`
  now opens through new `AbstractConnector.OpenOwnedTransaction[Async]`, which retries the open and BEGIN only. The
  write itself is not re-run: it carries the caller's store delegates, so re-running it is the boundary owner's
  decision. The server providers gain the same retry at open/BEGIN, where nothing has been sent.
- **Falsified, not adopted (rule 54):** that `Rollback()` on a deadlock victim's already-ended transaction throws and
  hides the 1205. A guard was written, then removed: with the plain `Rollback()` the live deadlock tests pass.

**CR-M144's claim is now true.** SQLite's bulk path retries busy/locked and converges, through the connector and
through
the stores. The rulebook line that cited it (§ the owned path is where providers differ) needed no change.

**TASK-258's criteria, answered:**

1. SQLite retries and converges: `BusyRetryTests`. Another connection holds the write lock past one attempt's 1 s busy
   timeout. Async store, sync store and the connector's own `BulkInsertAsync` each store all 25 rows, every Guid once.
   The control with `RetryPolicy.None` fails with SQLITE_BUSY and stores nothing.
2. The three servers make exactly one attempt on their own-connection bulk path: they pass `retryWhenOwned: false` at
   every bulk site (read). `RetryWhenOwnedTests.WithTheSHIPPEDDefaults_TheFlagDecides_AsyncHalf` and its sync twin pin
   true → 3 attempts and false → 1 under the shipped default. Not induced live per server.

**Live** (`DeadlockRetryLiveTests`, both on real servers): SQL Server 2022 and PostgreSQL, a real deadlock with the
framework as the victim. With the default policy: 2 attempts, both rows +1 exactly once (sync and async on SQL
Server). With `None`: the provider's own exception (1205 / 40P01), classified not-applied, nothing applied.

**The two alarm tests** were inverted, not deleted (rule 56): `WithTheSHIPPEDDefaults_…` now pin that the flag
decides. A third pins that a timeout after sending is not retried even with `retryWhenOwned: true`.

**Proven able to fail:**
- Drop 1205 from SQL Server's `IsNotAppliedException`: all 3 SQL Server live tests red.
- Drop 40P01 from PostgreSQL's: both PostgreSQL live tests red.
- Without `OpenOwnedTransaction` in `Atomically`: both store `BusyRetryTests` red, which was the first run.

**Regression:**
- All 35 test projects importing `Birko.Data.SQL` green, offline.
- Live SQL Server suites: `Birko.Data.SQL.MSSql.Tests` 224, `.MSSql.View.Tests` 22.
- Live PostgreSQL suites: `.PostgreSQL.Tests` 187, `.PostgreSQL.View.Tests` 22.
- MySQL not run live (no container); its codes are read from the MySQL error reference, not measured.

**Not done here (candidates, not filed):** `SqlUnitOfWork`'s own BEGIN does not use `OpenOwnedTransaction`; a deadlock
*inside* a store's bulk boundary is still not retried (re-running the write is the owner's call).

## Out of scope

- **The `retryWhenOwned` flag's structure.** [[TASK-258]] verified it — the participating path never
  retries, on either half, mutation-proven — and closed on the narrower scope.
- **Retry in any non-SQL backend.** `Birko.Contracts.RetryPolicy` is also used by `Birko.BackgroundJobs`
  and others; this task is the SQL connector default only. If the decision should be family-wide, that is
  its own spawn.
- **The exception-classification predicates.** `IsTransientException` and its provider overrides are
  TASK-291/294's subject and are correct as of that change.

## Human test plan

- [x] N/A — mechanical; the proof is an attempt count and the resulting row set under an induced transient
      error, per provider and per half.

## Follow-up 2026-11-06 — the live deadlock tests raced on a slow runner

CI live-tests on `da4eeecc` failed `Birko.Data.SQL.PostgreSQL.Tests.DeadlockRetryLiveTests.With_retry_off_the_deadlock_reaches_the_caller_and_nothing_is_applied`
(186/187; CI runs `-v q`, so no message was kept). Not reproduced locally (0/20 under 8 CPU burners). The tests arranged
the deadlock with a FIXED sleep in the other session (PostgreSQL 600 ms, SQL Server 300 ms): if the framework had not yet
taken row 1 by then, the other session took it first, no deadlock formed, and the write simply succeeded. Reproduced by
delaying the framework's start 800 ms: the PostgreSQL retry-off test fails with "no exception was thrown" (the CI
symptom) and the retry-on one with 1 attempt; all 3 SQL Server tests fail the same way. Fixed: the other session now
waits until the database shows the framework holding row 1 (PostgreSQL: its statement waiting on a lock; SQL Server: in
its `WAITFOR` or a lock wait) before closing the cycle. With the same 800 ms delay all 5 pass; live PostgreSQL 187/187,
SQL Server 224/224.
