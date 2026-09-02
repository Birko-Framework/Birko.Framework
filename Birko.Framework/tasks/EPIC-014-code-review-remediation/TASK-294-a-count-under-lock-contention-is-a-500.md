---
id: TASK-294
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-277, TASK-285, TASK-290, TASK-291]
findings: []
pr: (see the shared close section; one change in Birko.Data.SQL + one test commit)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.SqLite]
---

# A count that hits lock contention is a 500, while a count of a missing table is `0`

## Measured, not hypothesised

Found while running [[TASK-290]]'s cold-table storm — 200 distinct cold entity types, one connector, one
SQLite file, all first-touched at once. It reaches its condition (`created=200`) and records **0** schema
escapes, and what it produces instead, on **every** run, is 6-7 of these:

```
[read-storm] escapes=0 generation=0 created=200 failures=6 indexFailures=0
  FAIL x1 Exception: SELECT count(*) as count FROM "Probe100" AS Probe100
          <- SqliteException[sqlite 5]: SQLite Error 5: 'database is locked'.
  FAIL x1 SqliteException[sqlite 5]: SQLite Error 5: 'database is locked'.
```

Every one is a **count**, and every one reaches the caller as a fault. The chain is short and none of the
three links is wrong on its own:

- `AbstractConnector.EnsureSchemaAndReport` rewraps **any** exception as `new Exception(commandText, ex)`
  and throws (TASK-277, deliberate — a write must report);
- `IsMissingTableException` is false for `SQLITE_BUSY`, correctly, so `SelectCount`'s
  `catch … when (IsMissingTableExceptionChain(ex))` does not fire;
- `AbstractConnectorBase.RetryPolicy` defaults to **`RetryPolicy.None`**, so `ExecuteWithRetry` runs the
  action once.

⚠ The one failure that arrives **unwrapped** is worth noting separately: it comes from outside
`RunCommandTransaction`'s `try`, i.e. from `db.Open()` or `db.BeginTransaction()`, so it never reaches
`InitException` at all. Any fix has to cover both shapes or it covers the common one and misses the
first-connection one.

## Why it is worth an id

[[TASK-285]] made a count of a **missing** table answer `0` because *"a COUNT is a read, and a reader
already answers this condition with `yield break`"*. That argument does not distinguish the two failures
a reader can hit: `RunReaderCommand` swallows only a missing table and faults on BUSY too — so the
asymmetry TASK-285 removed for one condition is still there for the other, and it lands on exactly the
route the consumer's escapes came from (a GET list route's total-count query) under exactly the condition
that produces them (load).

**Note what the framework's own timeout does and does not buy.** `SqLiteSettings.GetConnectionString()`
emits `Default Timeout=30`, and that is honoured — the storm's Error 5 failures arrive only after roughly
30 s of waiting, which is why a 200-table storm takes ~33 s. So BUSY is absorbed below that ceiling and a
zero count of `Error 5` means contention never exceeded it, **not** that there was none. That correction
is recorded on TASK-290; here it is the reason this defect is invisible until a database is genuinely
saturated.

## The decision this task owns, and the answer it must NOT take

⚠ **Answering `0` for a busy database would be strictly worse than the 500 and must not be the fix.** A
missing table has no rows, so `0` is the true answer; a locked database has an unknown number, so `0` is
a fabrication — the silent-wrong-answer class § Conventions exists to forbid. Do not widen
`SelectCount`'s catch to transient errors.

The real candidates, to be priced against a measurement rather than chosen from taste:

1. **Retry the transient at the count path** (or make some non-`None` default policy the framework's).
   `IsTransientException` already classifies 5 and 6 on SQLite. Cost: a busy database turns a fast 500
   into a slow one, and the policy is process-wide surface a consumer may already be setting.
2. **Leave the fault and make it a better one** — a typed exception rather than a bare `Exception`
   wrapping a `SqliteException`, so a host can map it to 503 rather than 500. Cheapest, changes no timing,
   and honest: the request genuinely could not be served.
3. **Do nothing, and record that a saturated database faults.** Defensible, but then say so where a
   consumer reads it, because TASK-285's own wording invites the opposite expectation.

Whichever is chosen, `EnsureSchemaAndReport`'s rewrap is the place the type is currently destroyed — the
same line [[TASK-291]] is about for cancellation. The two should probably be decided together; they are
filed apart because a cancellation is the caller's own decision and a lock timeout is not.

## Acceptance

- [ ] Measured first: with a write lock held on a SQLite file, a `CountAsync` through a store faults with
      `SQLITE_BUSY` after the command timeout, on both the wrapped and the unwrapped paths.
- [ ] The chosen answer is stated with its cost, and `0` is explicitly not it.
- [ ] TASK-285's contract is untouched: a count of a genuinely missing table still answers `0`, asserted
      in the same suite so the two conditions cannot be conflated by a later edit.
- [ ] Mutation-proven.

---

## Closed 2026-09-02 — one change with [[TASK-291]], because it is one line

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
