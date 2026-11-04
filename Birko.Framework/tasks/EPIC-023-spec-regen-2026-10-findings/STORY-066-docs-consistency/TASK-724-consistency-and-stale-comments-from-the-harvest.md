---
id: TASK-724
parent: STORY-066
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-19]
pr: null
github-issue: null
jira-key: null
---

# Consistency gaps and stale comments the harvest found

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. Smaller items, each to confirm or close: async `CreateTableAsync` lacks the composite primary key, auto-increment refusal and `RecordTableCreated`; `SchemaDriftHealthCheck` can report Unhealthy though documented Degraded-only; `InsertAsync`/`SelectCountAsync` ignore `ct` for lock and open; async `SelectCountAsync(tables)` keeps null tables; sync and async inserts on one async connector use different locks; RavenDB settings never use user/password/secure; InfluxDB judges a failure transient by message substring; nullable aggregate view columns; a persistent view without a name counts on-the-fly; the instrumented store wrappers break aggregation above them; stale comments in `FieldBuilder` (HasPrecision 'NOT applied'), `ColumnDrift.CaseMismatch`, `SqLiteSettings` (cache id), `SqlOutboxStore` (lease).

Evidence: see the TASK-696 harvest reports; files named in the spec diffs of 22a2bb77.

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

| # | Claim | Result |
|---|---|---|
| 1 | Async `CreateTableAsync` lacks the composite key, the auto-increment refusal and `RecordTableCreated` | **reproduced:** on SQLite the table was not in `TablesCreated`; on SQL Server its own `CREATE TABLE IF NOT EXISTS` failed (`Incorrect syntax near the keyword 'IF'`) |
| 2 | `SchemaDriftHealthCheck` reports Unhealthy though documented Degraded-only | **reproduced:** a null connector and a throwing check were `Unhealthy` |
| 3 | `InsertAsync` / `SelectCountAsync` ignore `ct` for lock and open | **reproduced** for the insert: a cancelled insert behind a held write gate waited 4 s, the holder's whole hold. The count's open (no lock) took the token's absence too; SQLite opens locally, so not observable there; fixed with the insert |
| 4 | Async `SelectCountAsync(tables)` keeps null tables | **reproduced:** `NullReferenceException` |
| 5 | Sync and async inserts on one async connector use different locks | **true, recorded, not changed:** the sync monitor is reentrant and a shared `SemaphoreSlim` would deadlock a locked command that issues another; the async stores use only the async path |
| 6 | RavenDB settings never use user / password / secure | **reproduced:** a user name and password were accepted and ignored — refused now. `UseSecure`: the URL's scheme decides, recorded |
| 7 | InfluxDB judges a failure transient by message substring | **reproduced:** any message holding 429, 503 or "unavailable" was transient; a real 503 `HttpException` (message "refused") was not |
| 8 | Nullable aggregate view columns | **reproduced:** a string `MIN` over only NULLs threw in `GetString` |
| 9 | A persistent view without a name counts on-the-fly | **true and unreachable:** a view's name comes from its tables, so only a table-less view is nameless, and it fails first. The fallback (which used persistent-resolved conditions on the on-the-fly count) removed, rule 53 |
| 10 | The instrumented store wrappers break aggregation above them | **reproduced:** neither bulk wrapper implemented aggregation |
| 11 | Stale `FieldBuilder.HasPrecision` "NOT applied" | **true:** TASK-692 applies it; corrected (and `HasScale`) |
| 12 | Stale `ColumnDrift.CaseMismatch` | **true:** "never actioned" — `PlanRepair` writes a rename (TASK-633); corrected |
| 13 | Stale `SqLiteSettings` cache-id comment | **true:** the id also carries the schema; corrected |
| 14 | Stale `SqlOutboxStore` lease comment | **already fixed** by TASK-710 ("A claim is a lease"), closed |

## Implementation plan

The async `CreateTableAsync` overloads run the sync path. The schema-drift check answers `Degraded` where it answered
`Unhealthy`. `InsertAsync` and `SelectCountAsync` pass their token to the command funnel; the async count skips a null
table. RavenDB refuses credentials; InfluxDB classifies by `InfluxException.Status`; `StringFunction` reads `DBNull` as
null; the count's unreachable name test is removed (sync and async); the instrumented bulk wrappers forward aggregation;
four comments corrected.

## Built (2026-11-04)

18 test runs across SQLite, SQL Server (live), Health, InfluxDB, RavenDB, views and telemetry, 16 failing on the old
code (the rest guard the unchanged cases). Inverted (rule 56): the end-to-end test that pinned `Unhealthy` for a null
connector, and the pure-logic test that called a 429/503 message transient. Specs `schema-index-and-ddl.md`, `settings-configuration-chain.md`,
`views-and-aggregation.md`, `store-crud-contract.md`, `store-decorator-composition.md` updated (scoped; `generated-at`
left).
