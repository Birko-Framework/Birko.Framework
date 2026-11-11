---
id: TASK-901
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-891]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL views and bulk paths: leftovers after TASK-891

## Context

Found by TASK-891 (2026-11-10):
- **`AbstractConnector.ViewExists` throws instead of answering false** when a view is missing and the provider's View
  package is not loaded (seen on SQL Server without MSSql.View), so Auto mode fails rather than falling back:
  `EnsureSchemaAndReport` rewraps the error in a bare `Exception`, which the probe's `catch (DbException)` misses
  (rule 9).
- **Providers' `BulkInsert` / `BulkUpdate` / `BulkDelete(Type, …)`** (PostgreSQL, MySQL, MSSql) resolve through
  `DataBase.LoadTable`, not TASK-872's `LoadUsableTable`. Not measured; their WHERE names only Guid.
- **Auto mode caches a positive view-existence probe**, so a view dropped outside the connector keeps being read (now
  reported, since TASK-891) until `InvalidateViewExistsCache` is called.

## Acceptance criteria

- [x] Reproduce each (rule 54).
      → Reproduced 2026-11-11 on SQLite and live PostgreSQL, MySQL, SQL Server (composition: `Birko.Data.SQL.View`
        without the provider View package, as every consumer loads it). (1) `ViewExists(Async)` of a missing view threw a
        bare `Exception` (6 of 7 contract cases red per provider), so Auto reads and `CreateViewIfNotExists` failed; an
        unreachable database answered `False` (rule 26); each probe notified `OnException` and `OnInit`. Cause: TASK-717
        narrowed the catch to `DbException`, while the missing-table case arrives rewrapped by `EnsureSchemaAndReport`.
        (2) With the key column named after a value function (`User`, `CURRENT_USER`, `CURRENT_DATE`), the type-keyed
        `BulkUpdate`/`BulkDelete` (sync and async) rewrote / deleted 2 of 2 rows on all four providers (SQLite has the same
        code); non-key reserved columns failed or inserted. (3) After a view was dropped outside the connector, every Auto
        read failed until `InvalidateViewExistsCache`.
- [x] Fix or record each with its reason.
      → (1) The base probe reads `ViewProbeSql` through `RunReaderCommand(Async)` with `missingTableIsEmpty: true`:
        a row = exists, the reader's missing-table filter = missing, every other error reported as itself
        (`PostgresException 28P01`, ...); a missing answer reaches neither `OnException` nor `OnInit`; TASK-717's cancelled
        token still throws. The only `DbException` filters on a connector funnel were these probes (rule 9 sweep).
        (2) All 24 type-keyed bulk methods (4 providers × 3 × sync/async) resolve through `LoadUsableTable` (one producer);
        dead `table == null` checks removed (rule 53). (3) A persistent read or count that reports the view itself missing
        drops that cache entry (`ForgetViewIfReportedMissing`, a never-catching filter): the read still throws (TASK-891),
        the next one re-probes and reads on the fly; a missing source table does not clear it. Fallback inside the failing
        read (savepoint needed on PostgreSQL), a TTL, or no cache were laid out; kept the zero-cost fix — no consumer uses
        Auto mode.
- [x] Tests, red before each fix.
      → `ViewProbeContract` (8, in the 4 SQL providers), `RejectedColumnNameContract` bulk-by-type case (12 calls per
        provider), `Task891MissingViewReadTests` Auto case inverted (rule 56); red before, green after.
      → Live: all 35 importer suites of the changed shared projects green on the working tree; staged HEAD + patch snapshot: SQL.Tests 1148, SQL.Views 4051, SQLite 3182, PostgreSQL 2602 (one test failed once and passed on two reruns of the same snapshot, unidentified - the parallel-class interference of TASK-883), MySQL 2624, SQL Server 2658, MSSql.View 22, PostgreSQL.View 22, TimescaleDB 251.
      → Consumers: none uses Auto/Persistent view mode, the type-keyed `Bulk*` directly, or the cache invalidation; all
        load `Birko.Data.SQL.View` without the provider View package (so the base probe is theirs).
      → **Found, filed:** TASK-917 (the base `CreateView` renders `CREATE OR REPLACE VIEW`, rejected by SQLite and SQL
        Server; the base probe answers true for a same-named table; a swallowed probe error may abort a PostgreSQL
        transaction).

## Out of scope

- The items TASK-891 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
