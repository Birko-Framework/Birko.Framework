---
id: TASK-917
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-901]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL views without the provider View package: base DDL and probe gaps

## Context

Found by TASK-901 (2026-11-11). Every consumer loads `Birko.Data.SQL.View` without the provider View package (BardStudio,
Sandbox, DraCode, Presenter, Symbio, Reps), so the base implementation is what they run:
- **The base `CreateView` renders `CREATE OR REPLACE VIEW`**, which SQLite ("near OR") and SQL Server ("Incorrect syntax
  near 'REPLACE'") reject (measured; recorded in `ViewProbeContract.CreatesViewsWithoutViewPackage`). A persistent view
  cannot be created on those providers without the View package — fails loudly. Affects the SQLite-only consumers and
  Symbio's SQL Server path if they ever use persistent views (none does today).
- **The base probe answers `true` for a table with the view's name** (known from an older SQLite test comment).
- **Not measured (rule 54):** inside an ambient PostgreSQL transaction, the probe's swallowed missing-relation error
  probably aborts the transaction (25P02).

## Acceptance criteria

- [x] Reproduce each (rule 54).
      → Measured 2026-11-15 on SQLite and live PostgreSQL 16, MySQL 8.4, SQL Server 2022, base composition (no View
        package), new `ViewProbeContract` cases: `CreateView` failed on SQLite (`near "OR": syntax error`) and SQL Server
        (`Incorrect syntax near 'REPLACE'`); the probe answered `true` (sync and async) for a TABLE named like the view
        on **all four** (the task said "known from an SQLite comment"); and the suspected PostgreSQL case is **real**:
        after probing a missing view inside an ambient transaction, the next insert failed with `25P02 current
        transaction is aborted`. MySQL and SQL Server do not abort a transaction on that error; SQLite neither.
- [x] The base DDL works on every provider (dialect-rendered) or refuses by name pointing at the View package; the
      probe distinguishes a table; the transaction case fixed (savepoint) or refused. Tests red before.
      → Dialect-rendered: `SqlValueFragmentDialect_View.cs` adds `CreateOrReplaceViewSql` (default `CREATE OR REPLACE
        VIEW`; SQL Server `CREATE OR ALTER VIEW`; SQLite `DROP VIEW IF EXISTS …; CREATE VIEW …`) and `ViewExistsSql`, a
        parameterized catalogue query per provider (the queries the View packages already used). The dialect lives in
        each provider's BASE package, so every composition has it. One fix covers the other two: a catalogue query
        answers views only, and a missing view is no row, nothing swallowed, so no PostgreSQL transaction is aborted
        (no savepoint needed). Not `DROP` + `CREATE` everywhere: PostgreSQL refuses to drop a view another view depends
        on, and MySQL's replace would stop being atomic.
      → One producer (rules 16, 53): `BuildCreateViewSql` is no longer virtual and renders through the dialect; both
        probes share `PrepareViewExistsCommand`; the four View packages' copies of `BuildCreateViewSql` /
        `ViewExists(Async)` are deleted (they keep SQL Server indexed views and PostgreSQL materialized views).
      → Tests: 3 new `ViewProbeContract` cases (CreateView creates and replaces; a table is not a view; a probe inside
        a transaction leaves it usable), the SQLite / SQL Server opt-outs (`CreatesViewsWithoutViewPackage => false`)
        removed; red before as above, 11/11 on all four providers after. The contract's probe detector matched the old
        query's `1=0` and would have made "nothing probes" pass vacuously; it now matches the view name as a value.
        `ViewDdlTests`' two fake "provider-style" connectors (one still rendering the retired `CREATE VIEW IF NOT EXISTS`)
        replaced by a seam test; the MySQL View package's "override is declared here" test now pins the dialect's query.
      → Regression, all live (SQLite, PostgreSQL, MySQL, SQL Server, TimescaleDB): the 35 test projects importing
        Birko.Data.SQL / the view code all green (e.g. SQL.SqLite 3185, MSSql 2661, MySQL 2627, PostgreSQL 2605, Views
        4051, SQL.Tests 1147).
      → Docs: Birko.Data.SQL.View CLAUDE.md / README (its SQLite row was already stale), the four View packages'
        CLAUDE.md, MSSql.View README. Specs `views-and-aggregation` (the two view-DDL requirements re-read; the area's
        stamp left at its full harvest, recorded in its frontmatter) and a note in `filter-expression-translation`.

## Out of scope

- The items TASK-901 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
