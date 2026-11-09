---
id: TASK-872
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-862]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Reserved-word column names: leftovers after TASK-862

## Context

Found by TASK-862 (2026-11-08):
- **Direct connector calls skip the guard.** `connector.Select/Insert/Update/Delete(type, …)` on a table Birko did not
  build never runs store init, so a reserved column name is not refused. Measured with raw SQL: `WHERE User = 'postgres'`
  on PostgreSQL and `= 'dbo'` on SQL Server compare the session user, and a delete removed 2 of 2 rows; on SQLite
  `CURRENT_DATE` reads as the date. About 30 typed entry points would need the check.
- **MySQL 5.7:** `Birko.Data.SQL.MySQL/CLAUDE.md` declares 5.7 support, but the list is 8.4's, so it refuses `RANK`,
  which 5.7 accepts. Too strict, not unsafe.
- **SQL view stores** over a base entity are not checked on their own. Reads work on PostgreSQL and MySQL (qualified
  columns) and fail loudly on SQLite and SQL Server.

## Acceptance criteria

- [x] Decide per item whether it is reachable from a supported API and worth a guard (rule 54: reproduce first).
      → (a) **Reachable and silent** (measured 2026-11-09 through the public API, sync and async, on a table Birko did not
        build): the typed connector methods are public (`ISqlConnectedStore.Connector`, a SQL repository's `Connector`).
        With a column named `User` (PostgreSQL, SQL Server), `CURRENT_USER` (MySQL) or `CURRENT_DATE` (SQLite),
        `Delete(type, expr)` deleted 2 of 2 rows and `Update<T,P>(type, …, expr)` updated 2 of 2 where C# matches none:
        the WHERE names the column bare, so the server reads the function. Reads, inserts and model updates failed or
        were correct. **Fixed.**
      → (b) MySQL 5.7: no 5.7 server available, and 5.7 has no `information_schema.KEYWORDS` to measure. The 8.4 list
        over-refuses on 5.7 (the safe direction, the rename remedy works on both), the check runs before a connection so
        the version is unknown there, no consumer runs MySQL, 5.7 is past end of life. **Left, documented** in
        `Birko.Data.SQL.MySQL/CLAUDE.md`.
      → (c) View stores: the base entity's source columns are covered (Birko-built tables at CreateTable; persistent and
        on-the-fly DDL qualify source columns). **New silent case:** a persistent view emits its *own* column names bare
        (`AS User`, `WHERE User = …`). With a clean source (`Login` mapped to view property `User`), PostgreSQL and SQLite
        created the view and every read returned the function value; a filter matched 2 of 2 rows where C# expects 0.
        **Fixed.**
- [x] Each one fixed is tested red before the fix; each one left is recorded.
      → (a) One non-virtual funnel (rule 12), `LoadUsableTable(Type)` = `DataBase.LoadTable` + `RequireUsableColumnNames`,
        at every place a typed entry point resolves a type: 39 entry points through 20 call sites (Select, SelectCount,
        Insert, both Update forms, Delete, DeleteAll, SelectAggregate, sync and async), before any SQL. Not guarded on
        purpose: overloads taking a resolved `Table` or a table name (TASK-891), and DDL that names no column in a WHERE.
      → (c) Non-virtual `RequireUsableViewColumnNames(View)` in `BuildViewSelectSql` (every `CreateView`, PostgreSQL
        materialized views) and wherever a read chooses the persistent view.
      → Tests: `Task872DirectConnectorGuardTests` (39 refusals, 39 clean controls, not-virtual), the live contract test
        `A_direct_connector_call_on_a_table_Birko_did_not_build_refuses_by_name_and_writes_nothing` (11 calls, rows
        unchanged), `Task872ViewReservedColumnTests` (persistent DDL refused, outside-made view reads refused, on-the-fly
        still exact). Red with the production hunks reversed: 40/79, 1/12 per provider, 8/12. Green.
      → 35 importers live: green apart from the known PostgreSQL `With_retry_off`. Behaviour change: SQLite's direct
        `Insert` on a `CURRENT_DATE` column, which worked, is now refused like the store path.
      → **Found, filed:** TASK-891.

## Out of scope

- Quoting column identifiers (decided against in TASK-862).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
