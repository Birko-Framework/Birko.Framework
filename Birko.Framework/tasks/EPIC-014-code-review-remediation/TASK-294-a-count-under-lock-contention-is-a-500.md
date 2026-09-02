---
id: TASK-294
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-277, TASK-285, TASK-290, TASK-291]
findings: []
pr: null
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
