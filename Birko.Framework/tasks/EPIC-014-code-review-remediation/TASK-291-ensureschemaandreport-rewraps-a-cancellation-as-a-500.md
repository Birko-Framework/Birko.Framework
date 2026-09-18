---
id: TASK-291
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P3
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-277, TASK-286, TASK-290]
findings: []
pr: (see the shared close section; one change in Birko.Data.SQL + one test commit)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# `EnsureSchemaAndReport` rewraps a cancellation as a bare `Exception`, so a client that hung up becomes a 500

## Context

`AbstractConnector.EnsureSchemaAndReport` is the shared body of all four providers' `OnException`
handlers, and it rewraps **every** exception:

```csharp
var reported = new Exception(DescribeSchemaEscape(ex, commandText), ex);
RecordSchemaEscape(reported, CreatedTablesNamedIn(commandText).Select(kvp => kvp.Key));
throw reported;
```

`DescribeSchemaEscape` returns the command text unchanged when the failure is not a missing table, so a
`TaskCanceledException` / `OperationCanceledException` arrives at the host as
`Exception("SELECT ...", inner: TaskCanceledException)`. The **type is gone**, so nothing downstream can
tell "the caller went away" from "the database failed" — and a host that maps cancellation to 499 or to a
silent abort cannot select it. It surfaces as an unhandled 500.

Note the paths that reach here already treat cancellation as special *elsewhere*: the SQLite bulk
overloads carry an explicit `catch (OperationCanceledException) { rollback; throw; }` ahead of their
general catch, precisely so a cancellation is not routed through `InitException`. The single-command paths
(`RunCommand`, `RunCommandTransaction`, `RunCommandOn`) have no such arm, so their cancellations do get
rewrapped.

## Evidence, and its honest weight

Observed **six times** on the consumer side, all under Symbio's own 20 s `AbortSignal` during a 567-way
concurrency storm (Symbio TASK-602 round 7). So this is **induced, not field-reported** — the harness
aborted its own requests. It is recorded there as a warning to a future reader, because a grep for 500s
in those cycles finds these six and not the defect that run was actually about:

> WARNING: The "500s served" column of cycles 3 and 5 is NOT this defect and must not be counted as it.
> All six were `TaskCanceledException` — the harness's own 20 s `AbortSignal` firing under 567-way
> concurrency, rewrapped by `EnsureSchemaAndReport` and surfacing as `Unhandled exception`.

That is the real cost: not a broken request (the client had already gone), but a **diagnostic channel
polluted with noise that looks exactly like a server fault**, in the one condition where somebody is
reading the log carefully.

## The shape of the fix, and what it must not do

Let a cancellation through untouched, ahead of the rewrap:

- do not annotate it (`DescribeSchemaEscape` has nothing to say about it),
- do not record it as a schema escape,
- do not bump `SchemaGeneration` — TASK-288's invalidation is gated on the anomaly and a cancellation is
  not one,
- rethrow the original so the type survives.

WARNING: **it must not become a general "let some exceptions through" seam.** TASK-277 exists because these
handlers used to answer a missing table with a `return`, silently discarding a write and reporting success.
The distinction is that a cancellation is *the caller's own decision*, already reported to the caller by
its own token, so there is nothing to report a second time — whereas every database failure must keep
reporting. Any change here needs a test on **both** sides: a cancellation preserves its type and records
nothing, and a missing-table write still throws and still records.

Check the async twin (`AbstractAsyncConnector`) and all four provider handlers, not just the one that is
easy to reach — per this repo's repeated finding that a funnel with four overrides is not a funnel.

## Acceptance

- [ ] A cancelled command's `OperationCanceledException` / `TaskCanceledException` reaches the caller with
      its type intact, from both the sync and async single-command paths.
- [ ] It records nothing on `SchemaEscapes` and does not move `SchemaGeneration`.
- [ ] A missing-table **write** still throws and still records (TASK-277 / TASK-286 / TASK-288 unchanged),
      asserted in the same suite so the change cannot go one-sided.
- [ ] Mutation-proven: removing the cancellation arm reds the cancellation tests and nothing else.

---

## Closed 2026-09-02 — one change with [[TASK-294]], because it is one line

TASK-291 and TASK-294 were filed apart and are the same defect seen from two sides:
`EnsureSchemaAndReport` rewrapped **every** exception as `new Exception(DescribeSchemaEscape(ex, …), ex)`.
For a missing table that is the point — TASK-286's annotation rides on the message deliberately, because a
consumer's boot-time checks cannot see a runtime event. For everything else `DescribeSchemaEscape` returns
the command text unchanged, so the rewrap contributed **the SQL text and the loss of the exception's
type**.

### Step 0 — and it found a third consequence neither task had

Measured before a line changed (`RewrapClassificationTests`):

| given | surfaced |
|---|---|
| `OperationCanceledException` | `Exception: SELECT 1` — TASK-291 confirmed |
| `SqliteException` code 5 (BUSY) | `Exception: SELECT count(*) …`, and `IsTransientException` **false** |
| a missing-table failure | annotated and rewrapped — unchanged, must stay |

⚠ **The third consequence is the one that makes this more than a status code.**
`AbstractConnectorBase.ExecuteWithRetry` filters on `catch (Exception ex) when (… && IsTransientException(ex))`
— the **direct** predicate, not a chain walk. So a rewrapped `SQLITE_BUSY` was no longer transient and
**a `RetryPolicy` a consumer had configured silently never fired** on any failure raised inside the try.
Neither task file mentions retry; it fell out of enumerating the catch filters before writing anything,
which is § TASK-289's rule ("grep the filters before adding anything that can replace an exception")
applied in reverse.

### The fix

Report a non-missing-table failure **as itself**: attach the statement to `Exception.Data` under
`AbstractConnector.CommandTextDataKey` and rethrow via
`ExceptionDispatchInfo.Capture(ex).Throw()`. The missing-table branch is untouched.

Four properties, each with a test:

- **The type survives**, so `ExecuteWithRetry`, a host's `catch`, and cancellation handling all work again.
- **The statement survives**, on `Data` rather than in the message — otherwise this would trade one
  diagnostic for another.
- **Nothing is recorded on that branch**, and that is not an omission: `SchemaEscapes` and
  `SchemaGeneration` are gated on TASK-286's annotation, which only a missing-table failure can carry. A
  cancellation is not a schema anomaly.
- **`ExceptionDispatchInfo`, not `throw ex`**, so the original throw site survives. **Witnessed, not
  defensive**: the mutation reds exactly one test, and the stack head degrades from
  `Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC` to `EnsureSchemaAndReport`.

### TASK-294's own decision, answered

Its file listed three candidates and forbade one. The answer is **option 2 — leave the fault and make it a
better one** — reached for a reason its filing did not have: the fault is not merely poorly typed, it was
*silently disabling the retry that would have avoided it*. So:

- **`0` was never a candidate** and is not the fix: a locked database has an unknown number of rows, so
  answering `0` would be a fabrication. Unchanged.
- **No retry policy was added.** `RetryPolicy.None` is still the default; what changed is that a consumer
  who sets one now gets it. Choosing the framework's default retry behaviour is a separate decision with
  its own blast radius, and nothing measured here argues for it.
- ⚠ **And its premise is now much rarer, which is recorded rather than used to close it quietly.**
  TASK-296 put SQLite on WAL, where readers do not block writers, so the contention that produced its
  6-7 `Error 5` per storm run is largely gone. Reachability dropped; wrongness did not.

### The asymmetry that corrected this suite's first design

A lock taken **before** the statement never reaches the funnel at all: `RunCommandTransaction` opens the
connection and calls `BeginTransaction()` **outside** its `try`, and Microsoft.Data.Sqlite issues
`BEGIN IMMEDIATE` for its default isolation level, taking the write lock at once. Measured: **0**
`OnExecute` invocations for the INSERT (the command was never built) and a raw `SqliteException` code 5
surfacing after the retries — so that path already had a preserved type and already retried. The first
version of this suite tried to prove the retry defect there and was measuring the one path that was fine.
Pinned as the contrast.

### Measurements

`BIRKO_REQUIRE_LIVE` set, live PostgreSQL 16 / MySQL 8.4 / SQL Server 2022 / TimescaleDB 2 and on-disk
SQLite: **1,614 tests, 0 failed, 0 skipped** across eleven suites — `Birko.Data.SQL` 667, SqLite **337**
(331 → 337), PostgreSQL 98, MySQL 101, MSSql 111, TimescaleDB 55, Migrations.SQL 54,
Migrations.TimescaleDB 81, InMemory 69, JSON 23, XML 18.

**Mutations, disjoint:**

| mutation | red |
|---|---|
| restore the blanket rewrap | **4** — cancellation, transient classification, command text, and the end-to-end constraint violation; both contract pins (the missing-table annotation, the before-statement lock) stayed green |
| `throw ex` instead of `ExceptionDispatchInfo.Capture(ex).Throw()` | **1** — the stack-trace assertion, and the trace head visibly degrades |

⚠ **One unidentified failure, recorded rather than rounded off.** `Birko.Data.SQL.MSSql.Tests` failed
**1 of 111** once, during the eleven-suite sweep with four database containers running; its identity was
not captured, and 5 subsequent isolated runs were clean (111/111 each). Not attributable to this change on
the evidence available, and the same shape as [[TASK-276]]'s family — appended there rather than left as a
footnote here.

### Deliberately not done

- **No change to the framework's default `RetryPolicy`.** See above.
- **No change to the count path.** TASK-285's `0` for a missing table stands; a busy database now faults
  *with its own type*, which is what a host needs to map it to a 503 or retry it.
- **The four providers' bulk `catch (OperationCanceledException)` arms are untouched.** They already
  rethrow before reaching `InitException`, so they were never part of this; the defect was on the
  single-command paths.
