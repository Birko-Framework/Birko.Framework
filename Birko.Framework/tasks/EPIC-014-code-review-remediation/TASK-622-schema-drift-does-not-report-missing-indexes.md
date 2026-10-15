---
id: TASK-622
parent: EPIC-014
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
related: [TASK-595, TASK-204, TASK-269]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Schema drift does not report a declared index that the table lacks

## Context

Found 2026-10-15 reviewing schema-drift coverage. Index problems surface only through
`AbstractConnector.IndexCreationFailures`, which lists indexes schema-ensure **tried** to build and could not (TASK-204).
Schema-ensure runs `CREATE TABLE IF NOT EXISTS` once. An index the model gained after the table existed is created only
if something ensures indexes again, and an index dropped by hand is never noticed. `DetectDrift` compares columns
only, so a table with none of its declared indexes reports clean. TASK-595 measured that state for real:
`Birko.Models.Users.SQL` had zero indexes, and nothing said so.

## Acceptance criteria

- [x] Measured first: a table created before an index was declared (or with the index dropped) reports clean in
      `DetectDrift` and `SchemaDriftHealthCheck`. Record it, per provider.
      → Clean on all four (SQLite, PostgreSQL, MySQL, SQL Server): a framework-created table with one declared
      index dropped and the unique one recreated plain reported `IsClean`.
- [x] A declared index that is absent is reported, by name and columns, read from the provider's catalogue: the same
      `ListIndexesSql` the index managers use, so there is one reader. A present index with different columns or
      uniqueness is reported too.
      → `SchemaDriftReport.IndexDrifts` (`Missing`, `ColumnsDiffer`, `UniquenessDiffers`); `IsClean` needs it empty.
      Stored indexes come from each provider's index manager through a new `CreateIndexManager()` connector seam,
      so drift and the managers read indexes one way. Names are compared with the connector's identifier comparer,
      and columns in order, the declared names folded the way they are stored. **Found on the way:**
      `SqlIndexManager`'s row reader used `GetInt32` on SQL Server's `tinyint` `key_ordinal`, so `ListAsync` threw
      `InvalidCastException` on that provider. Fixed in the shared reader (`Convert.ToInt32`), because drift
      depends on it.
- [x] Undeclared indexes are not reported (DBAs add their own). Record the reason.
      → Recorded: a DBA's own index is legitimate, and reporting every undeclared one would be noise on every
      table. Partial-index predicates are not compared either (a declared predicate is still emitted at creation).
- [x] Live tests per SQL provider, proven red.
      → Per provider: missing and weakened indexes reported (red first), and a clean control. The health check
      counts index drift separately (`indexDrifted`, and "N declared index(es) differ" in its description), with a
      test of its own.

## Out of scope

- Creating the missing index. That is `EnsureIndexes`' (or schema-ensure's) job, not a report's.

## Human test plan

N/A — covered by the live provider tests.

## Implementation plan

1. **Measure** per SQL provider: create a table, then declare an index (or drop one that exists), and run
   `DetectDrift`. Expected: clean. Record it.
2. **Declared side**: `Tables.Table.Indexes` (attributes and fluent `HasIndex`, after TASK-595), the same declarations
   schema-ensure emits from (rule 63).
3. **Stored side, one reader**: move `ListIndexesSql` and its row reader from the `SqlIndexManager` dialects onto the
   connector (or have the connector call the dialect's manager). The index manager and the drift check then read
   indexes the same way, and the four dialect queries stay in one place each.
4. **Report** on `SchemaDriftReport` as a new `IndexDrifts` list (`Missing`, `ColumnsDiffer`, `UniquenessDiffers`), not
   mixed into column drifts. Match by name (the framework names every index it creates). Undeclared stored indexes
   are not reported: DBAs add their own, and reporting them would be noise on every table (recorded).
5. `SchemaDriftHealthCheck` counts index drift like column drift (Degraded).
6. Tests: live per provider (missing, columns differ, uniqueness differs, a clean control), each proven red.

## Progress log

- 2026-10-15 — Live PostgreSQL, MySQL and SQL Server (Docker), plus SQLite. Red: all four reported the altered table
  clean. Then SQL Server's index tests threw `InvalidCastException`, which exposed the `ListAsync` reader bug; fixed in
  the shared reader. Deviation from plan step 3: the listing SQL stays on the index managers, and the connector reaches
  them through `CreateIndexManager()`, rather than moving the queries onto the connector. One reader either way, with
  less churn. Final: 32 SQL-dependent projects, 2,531 tests, green live.
