---
id: TASK-306
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
related: [TASK-276, TASK-303, TASK-270, TASK-305]
findings: []
pr: null
github-issue: null
affects: [Birko.Data.SQL.MSSql, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MySQL]
---

# Every live provider suite runs ~19 parallel classes against ONE database, and two different failures follow

## Context — a fourth instance of [[TASK-276]]'s family, with the identity captured

Measured 2026-09-08 against live SQL Server 2022 (16.0.4265.3), 12 full-suite runs **under 8 CPU
burners** — the load lever TASK-276 identified and which no session had applied to this suite:

```
NullableUniqueColumnLiveTests.A_required_unique_column_keeps_its_inline_constraint
Microsoft.Data.SqlClient.SqlException : Transaction (Process ID 72) was deadlocked on lock
    resources with another process and has been chosen as the deadlock victim. Rerun the transaction.
```

**1 failure in 12 loaded runs; 0 in 12 idle runs of the same binary.** Every class in the suite builds
its settings from the same host/database/user/port, xUnit runs classes in parallel, and 13 of the 19
classes issue `DROP TABLE` / `CREATE TABLE` — so parallel DDL contends for the same catalogue locks.

The sharing is not MSSql-specific:

| suite | classes | database | classes issuing DROP |
|---|---|---|---|
| `Birko.Data.SQL.MSSql.Tests` | 19 | `birkoview` (single) | **13** |
| `Birko.Data.SQL.PostgreSQL.Tests` | 19 | `birkoview` (single) | **12** |
| `Birko.Data.SQL.MySQL.Tests` | 16 | `birkoview` (single) | **11** |

## Two distinct consequences of one cause, and only one of them is closed

1. **`SchemaGeneration` coupling — CLOSED for the one class it was measured on.** A sibling's
   deliberate schema escape bumps the shared connector's generation, so another class's store fails
   `CanTrustRememberedInitialization`, re-initialises, and re-creates a table that class had just
   dropped. Identified on TASK-276 (2026-09-07) and fixed 2026-09-08 by giving
   `SchemaEnsureRollbackResidueLiveTests` its own database, hence its own cached connector, with a
   deterministic test (`This_class_does_not_share_a_connector_with_the_rest_of_the_suite`).
2. **Lock contention — OPEN, and this task's subject.** A different mechanism entirely: no connector
   state involved, just two connections doing DDL against one database. The fix above does nothing for
   it, and the 12 clean post-fix runs are **not** evidence about it (at ~1 in 12 that is the expected
   outcome either way).

## Why this is a task rather than a side-fix

The remedy that works for one class does not scale by hand: a database per class means ~19 fixtures per
provider × 3 providers, and the shared `EnsureDatabase` helper each would need does not exist. The
alternatives each have a measured objection:

- ⚠ **A shared xUnit collection does not port from [[TASK-303]].** That fixed the TimescaleDB twin by
  serialising the 5 classes that touched one catalogue. Here the DDL-issuing set is **13 of 19**, which
  is `"parallelizeTestCollections": false` in all but name — the fix TASK-276 explicitly forbids,
  because it hides the coupling instead of removing it and leaves every other suite exposed.
- **A database per class** is immune by construction (a class added later cannot reach another's
  connector or its locks) and costs a `CREATE DATABASE` per class per run. Measure that cost before
  committing to it: on SQL Server it is not free, and 19 × 3 of them may dominate a suite that
  currently runs in ~1 s.
- **A schema per class** would isolate the locks more cheaply — but the framework has **no schema
  concept at all** ([[TASK-272]]), so there is nothing to hang it on today.

## Acceptance criteria

- [ ] Reproduce first, under load, with a `.trx` per run — TASK-276 records losing an identity twice in
      one day to a sweep loop that grepped only the summary line. An idle run measures nothing here:
      0/12 idle against 1/12 loaded is the whole reason this is filed.
- [ ] A decision on the isolation mechanism, with the per-run cost of the chosen one measured rather
      than assumed.
- [ ] Applied to all three provider suites, or applied to one with the other two explicitly deferred and
      the reason recorded — not left implied by a passing suite.
- [ ] Proven able to fail: the fix must be distinguishable from luck. A rate this low means a
      before/after of 12 runs cannot do it, so pin whatever is deterministic (e.g. that two classes
      resolve different databases) the way TASK-276's `SchemaGeneration` fix did.
- [ ] ⚠ Do not weaken any assertion. What these tests pin — inline `UNIQUE` constraints, index DDL,
      column types — is correct; only their isolation is wrong.

## Out of scope

- **The `SchemaGeneration` coupling**, closed above for the class it was measured on. If it turns up on
  another class, that class gets the same treatment; it is not a reason to reopen this.
- **The framework's retry behaviour.** A deadlock victim is the textbook transient and
  `MSSqlConnector.IsTransientException` already enumerates `1205` — it is not retried only because the
  default `RetryPolicy` is `None`. That is [[TASK-305]]'s decision, and this deadlock is recorded there
  as its first measured motivation. ⚠ **A retry would mask this flake rather than fix it**, so do not
  reach for TASK-305 as the answer here.
- First-class schema support — [[TASK-272]].

## Human test plan

- [ ] N/A — mechanical; the proof is a loaded repeated-run measurement plus a deterministic isolation
      assertion.
