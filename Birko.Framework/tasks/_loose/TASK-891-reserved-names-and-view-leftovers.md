---
id: TASK-891
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-872]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Reserved names and persistent views: leftovers after TASK-872

## Context

Found by TASK-872 (2026-11-09):
- **A failed `EnsureAsync` followed by a persistent view read silently returns 0 rows** on SQL Server and MySQL, while
  Count throws "Invalid object name": the reader swallows a missing table (rule 26). Not reserved-word specific.
  Measured.
- **`ViewSqlGenerator` (migrations) emits view columns bare with no provider list**: a generated persistent view with a
  reserved view property would read the database function silently on PostgreSQL and SQLite (reasoned from TASK-872's
  measurement of the same shape).
- **Connector overloads taking a resolved `Tables.Table` or a table name** (`Delete(Table, …)`, `Insert(string, …)`,
  `Update(string, …)`, `SelectCount(IEnumerable<string>)`) are unguarded; a direct `Delete` through
  `DataBase.LoadTable(t)` is still silent.
- `if (table == null) yield break;` in `SelectAggregate(Async)` is unreachable (rule 53).

## Acceptance criteria

- [x] Reproduce each (rule 54).
      → Measured 2026-11-10 live on all four providers:
        (1) A missing view read answered 0 rows on **all four** (not only SQL Server and MySQL), in three scenarios — a
        persistent view never created, an Auto-mode view dropped after it was seen, an on-the-fly view whose source table
        is missing — on all five read paths, while `CountAsync` threw the annotated missing-table exception.
        (2) Migrations' `ViewSqlGenerator`: PostgreSQL (`User`) and SQLite (`CURRENT_DATE`) created the view, and a bare
        read returned the function value ('postgres', today's date) instead of 'alice'; MySQL and SQL Server failed.
        (3) `Table` and table-name overloads: `Delete(Table)` deleted 2 of 2 rows and every `Update(Table or name, …)`
        rewrote 2 of 2 on a reserved column.
- [x] The missing-table read reports (or heals) consistently with Count; the migrations generator uses the provider's
      list; decide whether the `Table` / name overloads are a supported API and guard them through TASK-872's funnel or
      record why not; remove the dead branch.
      → (1) The reader's missing-table catch exists for table reads, which a store heals by creating the table (TASK-285);
        nothing creates a view on a read. `RunReaderCommand(On)` / `ReadOnAsync` take `missingTableIsEmpty` (default
        true); only the view `Select` paths pass false, so a missing view throws what the count throws. Table reads keep
        healing (rule 26).
      → (2) `GenerateCreateViewSql` takes the migration context's connector and runs its `RequireUsableViewColumnNames`
        (one producer); without a connector nothing is checked (documented).
      → (3) Supported public API (a store's bulk update runs through `Update(string, …)`), so guarded: `Table` overloads
        run non-virtual `RequireUsableTable` (which `LoadUsableTable` now calls); the name-level funnels run non-virtual
        `RequireUsableStatementColumns` over written columns and filter names at any depth, bare or self-qualified.
        **Breaking:** `InsertAsync(string, …)` is no longer virtual (rule 12; no override exists).
      → (4) The unreachable `if (table == null) yield break;` removed from `SelectAggregate(Async)` (rule 53).
- [x] Tests, red before each fix.
      → `Task891MissingViewReadTests` red 12/16 → 16/16; `Task891MigrationViewReservedColumnTests` red 8/12 → 12/12;
        `Task891TableAndNameOverloadGuardTests` red 42/84 → 85/85; contract fact
        `A_Table_or_table_name_connector_call_refuses_a_rejected_column_and_writes_nothing` red on each provider → green.
      → All 35 importers live green on HEAD + TASK-892 + this change (SQL.Tests 1112, SQLite 2280, PostgreSQL 1700, MySQL
        1722, SQL Server 1756, Views 3317, View.Migrations 28). No consumer reads SQL views or calls these overloads.
      → **Found, filed:** TASK-901.

## Out of scope

- The typed entry points and view DDL (TASK-872, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
