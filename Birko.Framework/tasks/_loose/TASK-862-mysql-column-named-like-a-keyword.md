---
id: TASK-862
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A column named like a MySQL keyword (`Rank`) breaks CREATE TABLE

## Context

Found by TASK-859 (2026-11-07), on MySQL live. A property named `Rank` makes CREATE TABLE fail, because column names
are never quoted. That is deliberate, per rule 17 ("quote table identifiers; never quote column identifiers"), but it
leaves keyword-named columns unusable on MySQL. The failure is loud.

## Acceptance criteria

- [x] Reproduced on each provider with a set of keyword-named properties (`Rank`, `Order`, `Group`, `Key`, `Value`,
      `User`). Record which fail where, in DDL, reads, writes and filters.
      → Measured 2026-11-08 live through the stores (DDL, insert, read, filter, count, update, update-by-filter,
        delete, delete-by-filter), on SQLite 3.53.3, PostgreSQL 16.15, MySQL 8.4.11 and SQL Server 2022:
        - `Order`, `Group`, `Select`, `Table` fail at DDL everywhere. `Rank` fails on MySQL only; `Key` on MySQL and
          SQL Server; `User` on PostgreSQL and SQL Server; `Desc` on all but SQLite; `Index` on all but PostgreSQL.
          `Value` works everywhere.
        - Full sweep of 1,030 candidate names: MySQL rejects exactly its 262 `RESERVED` words; PostgreSQL 107 (catcodes
          R and T plus 6 system columns); SQL Server 179 of its 185 documented reserved words; SQLite 58 at DDL plus 5
          that pass DDL and break writes.
        - **Silent on SQLite:** `CURRENT_DATE`, `CURRENT_TIME`, `CURRENT_TIMESTAMP` pass DDL and reads, but in a write's
          WHERE they read as the current date, so a filtered update or delete matched 0 rows with no error.
        - A table that already exists: every store's lazy init re-runs `CREATE TABLE IF NOT EXISTS`, so stores failed
          loudly. Direct connector calls on PostgreSQL and MySQL read correctly (qualified `T.Order` is legal); with raw
          SQL, `WHERE User = 'postgres'` compared `current_user` and deleted 2 of 2 rows.
- [x] **Decide with the user:** quote column identifiers per provider (revisiting rule 17 and its measurement), or
      refuse a keyword-named column by name when the table is built, with the provider's keyword list. Record the
      decision.
      → **Decided 2026-11-08 by the user: option A, refuse by name.** Column identifiers stay unquoted (rule 17).
        When the table is built, a column name that the provider rejects (its *reserved* words, measured live, not
        the full keyword list) is refused with `NotSupportedException` naming the column, entity and provider,
        before any SQL is sent. The caller maps the property to another column name. Refusal is per provider.
      → Built: `RejectedColumnNames` (provider, measured version, the set), one per provider in
        `*Connector_ColumnNames.cs` (rule 20); TimescaleDB inherits PostgreSQL's. Non-virtual
        `RequireUsableColumnNames` on `AbstractConnectorBase` (rule 12), called by `CreateTable(IDictionary)` for every
        table of a batch before the first is created, and by `AlterTableAdd(Async)`. `PlanRepair` skips such a column;
        the migrations `SqlSchemaBuilder` checks `CreateCollection(..).Build()` and `RenameField`. Thrown before the DDL
        wrapper, so the type survives. Renaming through `[NamedField]` / `[Column]` and `HasColumnName` round-trips on
        all four providers; no new mapping API.
- [x] Tests per provider, red before the fix.
      → `tests/Shared/StoreContract/RejectedColumnNameContract.cs` (11 tests, linked into the four provider suites),
        including `Every_refused_name_is_rejected_by_the_server`, which re-measures each list live.
        `RejectedColumnNameGuardTests` (SQL.Tests, 5) and `RejectedColumnNameMigrationTests` (Migrations.SQL, 3).
      → Red with the guard off: SQLite 5/11, PostgreSQL 5/11, MySQL 6/11, SQL Server 5/11, SQL.Tests 2/5,
        Migrations 2/3. The rest pin that nothing is over-refused.
      → 35 importer suites live with `BIRKO_REQUIRE_LIVE=1` (TimescaleDB included): green, apart from the known
        PostgreSQL `With_retry_off`.
      → Consumers: no entity is newly refused on the provider it uses. On MySQL or SQL Server, Symbio's `Key`, `Read`
        and `Condition` columns would be.
      → **Found, filed:** TASK-872 (direct connector calls skip the guard; the MySQL list is 8.4's and over-refuses
        `RANK` on 5.7; view stores are not checked separately).

## Out of scope

- Cosmos DB view aliases (TASK-844, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
