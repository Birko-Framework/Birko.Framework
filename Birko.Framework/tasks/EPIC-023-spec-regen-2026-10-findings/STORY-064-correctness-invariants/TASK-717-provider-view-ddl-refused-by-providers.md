---
id: TASK-717
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-12]
pr: null
github-issue: null
jira-key: null
---

# SQL view DDL the providers refuse

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `ViewSqlGenerator` always emits `CREATE OR REPLACE VIEW`, refused by SQLite and SQL Server. SQLite's own view DDL uses `CREATE VIEW IF NOT EXISTS`, which never replaces. The SQL Server indexed-view clustered index names table-qualified source columns a view does not have. A bool join constant renders `TRUE`/`FALSE`, refused by SQL Server. The base async `ViewExists` swallows every exception and the async read funnels call the sync one.

Evidence: Birko.Data.Migrations.SQL/.../ViewSqlGenerator.cs:61; SqLiteConnector_View.cs:17; MSSqlConnector_View.cs:100,164; ViewSelectSqlBuilder.cs:204; AbstractConnector_CreateView.cs:128-142.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

`tests/Birko.Data.SQL.Views.Tests/Task717ViewDdlLiveTests.cs` on SQLite and live PostgreSQL 16 and SQL Server 2022
(MySQL not run), and `tests/Birko.Data.SQL.View.Migrations.Tests/Task717MigrationViewLiveTests.cs` on SQLite.

| Claim | Result |
|---|---|
| `ViewSqlGenerator` emits `CREATE OR REPLACE VIEW`, refused by SQLite and SQL Server | **reproduced** on SQLite through `context.CreateView`: `near "OR": syntax error` (SQL Server's T-SQL has no such form; not run there) |
| SQLite's `CREATE VIEW IF NOT EXISTS` never replaces | **reproduced:** a second definition under one name left the first in place, sync and async, no error |
| The indexed view's clustered index names table-qualified source columns | **reproduced:** `Incorrect syntax near '.'` at the index step |
| A bool join constant renders `TRUE`, refused by SQL Server | **reproduced:** `Invalid column name 'TRUE'` |
| The base async `ViewExists` swallows every exception | **reproduced** on PostgreSQL and SQL Server (no async override): a same-named table answered true, a cancelled token answered false |
| The async read funnels call the sync `ViewExists` | **reproduced:** an Auto read and count made 2 sync calls, 0 async |
| Found: PostgreSQL's `CREATE OR REPLACE VIEW` refuses a definition with other columns | **recorded, not changed:** `42P16`, an error in the open; `RecreateView` covers it, and a drop-first default would break views other views depend on |

## Implementation plan

SQLite's create becomes `DROP VIEW IF EXISTS …; CREATE VIEW …` in `CreateView`'s own transaction. A connector hook
`ViewBoolLiteral` (`1`/`0` on SQL Server) reaches `ViewSelectSqlBuilder` as an optional `boolLiteral`. The indexed
view's key columns are the view's own columns (the field's property). PostgreSQL and SQL Server get catalogue
`ViewExistsAsync` overrides; the base probe answers false only for a `DbException`. The async funnels use a new
`ShouldUsePersistentViewAsync` sharing the cache. The generator emits `CREATE VIEW`, and the migration extension
drops first.

## Built (2026-11-04)

9 tests: 6 run on each provider (SQLite, PostgreSQL, SQL Server; MySQL skips without a server), an indexed
view on SQL Server, the async funnel and the migration on SQLite; 11 runs fail on the old code. Inverted (rule 56): the SQLite DDL
string, the generator's statement form, the extension's command, and the indexed view's qualified key columns.
SQL.Views 124, SQL.View.Migrations 16, SQL 723, SqLite.View 10, MSSql.View 22, PostgreSQL.View 22, MySQL.View 7,
SQL.ViewModel 20 green with PostgreSQL and SQL Server live, and the 27 other suites importing the changed projects.
Specs `views-and-aggregation.md` and `migrations.md` updated (scoped; `generated-at` left).
