---
id: TASK-883
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-864]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Live SQL contract suites share fixed table names, so concurrent runs collide

## Context

Found by TASK-864, TASK-866 and TASK-874 (2026-11-09): the shared StoreContract fixtures use fixed table names
(`T869TextRows`, `T860CallRows`, `T866CollapsedRows`…). Two test runs against the same live server at once drop and
refill each other's tables: duplicate keys, "Invalid object name", schema-change errors, the PostgreSQL `pg_type`
unique violation. Test infrastructure only, but it makes parallel and CI runs flaky.

Also (TASK-894, 2026-11-10): MySQL's `EscapeAnomalyDiscriminationLiveTests.TASK295_…` and
`SchemaEnsureRollbackResidueLiveTests.A_write_to_a_missing_table_fails…` fail intermittently only when the healing
classes run in parallel (HEAD 3/8, run alone 6/6): cross-class interference on the shared cached connector (rule 61).

## Acceptance criteria

- [x] Reproduce with two concurrent runs of one provider suite.
      → Reproduced 2026-11-11: two concurrent PostgreSQL runs at HEAD failed 1571 and 1570 of 2602 (273 ×
        `pg_type_typname_nsp_index` duplicates, schema-ensure failures on the shared `T8xx` tables, unique violations, rows
        counted twice); alone 2602/2602. MySQL's raw-dropping classes failed 3 of 8 runs
        (`EscapeAnomalyDiscriminationLiveTests.TASK295_…`), the same test as today's one-off PostgreSQL flake.
- [x] Each run uses its own table names (or schema / database), from one producer in the shared fixtures, and drops
      them afterwards.
      → One producer `tests/Shared/TestSupport/LiveDatabase.cs`: `LiveDatabase.Name("PG"|"MYSQL"|"MSSQL"|"TS")` creates
        `{BIRKO_*_DB or birkoview}_r<yyMMddHHmmss><4 hex>` on first use (lower-case, under every identifier limit;
        TimescaleDB gets its extension; with no host it returns the configured name and never connects). A database per run
        rather than suffixed names: `[Table]` attributes cannot take a suffix, and indexes, constraints, views, migration
        bookkeeping and literal SQL would each need their own. Wired by one `LiveDatabase.props` import per live csproj
        (14); ~136 files reroute their database name; extra fixed databases/schemas use `LiveDatabase.RunName`.
        `LiveTestFramework` drops the run's databases in `BeforeTestAssemblyFinishedAsync` (ProcessExit and framework
        disposal measured too late — vstest kills the testhost); tagged databases older than 6 h are swept. Servers back
        to their baseline database list after all runs.
      → Cross-class interference: `DataBase.GetConnector` caches one connector per settings id, and a raw drop by a
        sibling bumps its `SchemaGeneration` (TASK-288 healing), so an expected failure healed. The victim classes take
        their own database via `LiveDatabase.Dedicated` (as TASK-276; a collection rejected there). MySQL's two
        `SET GLOBAL time_zone` tests serialise on a server-wide `GET_LOCK`.
- [x] Two concurrent runs pass.
      → Concurrent pairs after: PostgreSQL 3 (2602/2602 each), MySQL 2 after the lock fix (2624/2624), SQL Server 3
        (2658/2658), TimescaleDB 3 (251/251); MySQL and PostgreSQL raw-drop classes 8/8. Single runs green:
        BackgroundJobs.SQL 68, EventSourcing.SQL 26, Localization.SQL 148, Migrations.SQL 267, Migrations.TimescaleDB 90,
        PostgreSQL.View 22, Providers 40, View.Migrations 47, SQL.Views 4051, Security.Grants.SQL 27.
      → Staged HEAD + patch snapshot, single runs live: PostgreSQL 2602, MySQL 2624, SQL Server 2658, TimescaleDB 251, SQL.Views 4051 - all green; no production file changed.
      → **Found, filed:** TASK-918 (MySQL `ClearAllPools` and the global time-zone window mid-run; `TestTempDirectory`'s
        ProcessExit cleanup likely dead; TimescaleDB twin could use `Dedicated`; docs and live-tests.yml still describe a
        shared `birkoview`; old leftover databases on the local servers).

## Out of scope

- Production code.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
