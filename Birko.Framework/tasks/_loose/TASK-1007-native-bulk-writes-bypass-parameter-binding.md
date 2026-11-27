---
id: TASK-1007
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-852, TASK-854]
findings: []
pr: null
github-issue: null
jira-key: null
---

# MySQL, PostgreSQL and SQLite native BulkUpdate/BulkDelete assign .Value instead of binding

## Context

Found at the 2026-11-27 `/specs regen` of `backend-store-implementations`; the user asked for it to be filed.

[[TASK-854]] moved SQL Server's native `BulkUpdate` / `BulkDelete` onto `AddParameter`. Rule 28 is why: a value the
driver infers a type for and a value the framework types explicitly are two producers.

Before TASK-854, SQL Server built placeholder parameters and assigned `.Value` per row. Measured:
- `BulkUpdate` stored `.1234567` as `.1233333`;
- a composite-key `BulkDelete` holding a `DateTime` deleted nothing.

The other three providers still assign `.Value`:
- `Birko.Data.SQL.MySQL/Database/Connectors/MySQLConnector.cs`: lines 863, 867, 941, 945, 1008, 1066.
- `Birko.Data.SQL.PostgreSQL/Database/Connectors/PostgreSQLConnector.cs`: lines 796, 800, 874, 878, and the delete paths.
- SQLite's native bulk paths: not checked yet.

Assigning `.Value` skips `ConvertParameterValue` and `CreateParameter`. Three consequences may follow, none measured yet:
- PostgreSQL's `DateTimeKind` strip (in `ConvertParameterValue`) may not apply to these paths.
- SQLite's Guid-as-string binding (also in `ConvertParameterValue`) may not apply either.
- MySQL's inferred `DateTime` type may lose precision, as SQL Server's did.

## Acceptance criteria

- [x] Red first, on each of the three providers, live on Docker: a `BulkUpdate` and a composite-key `BulkDelete` whose
  values depend on the framework's conversion. At least:
  - a `DateTime` with seven fractional digits;
  - a Guid key on SQLite;
  - a `DateTimeKind.Utc` value on PostgreSQL.

  Record which fail.
- [x] Every row's SET and key values bind through `AddParameter`, as SQL Server's do.
- [x] Green on all three, and the SQL suites pass live with `BIRKO_REQUIRE_LIVE=1`.

## Evidence 2026-11-27

Measured with a shared `BulkBindingContract` (`tests/Shared/StoreContract/`), linked into the four SQL provider test
projects. It checks that a collection update stores what a single update stores (a 7-digit, `Kind=Utc` `DateTime` and an
enum), and that a collection delete matches a composite `DateTime` + enum key as a single delete does. Sync and async;
live on Docker with `BIRKO_REQUIRE_LIVE=1`.

**Before the fix:**

| Provider | Result |
|---|---|
| PostgreSQL | 2/4 fail: the bulk delete threw `InvalidCastException`, because an enum key reached Npgsql unconverted (`Property.GetValue` → `.Value`). `BulkUpdate` reads its key the same way. |
| MySQL, SQLite | 4/4. The drivers accept a raw enum; SQLite's own `ConvertPrimaryKeyValue` covered Guid keys. No wrong result was measurable, so for them the change is rule 28 conformance. |
| SQL Server | 4/4 (the control; fixed in TASK-854). |

The `DateTime` precision and `Kind` hypotheses in the Context did not hold. PostgreSQL's `Bulk_update_does_not_shift_…`
already pinned the kind, through `Prepare()`, and MySQL kept the precision.

**Fix.** All three bind each row through `AddParameter`; SQLite's `ConvertPrimaryKeyValue` is deleted.

- On PostgreSQL, `AddParameter` after `Prepare()` was measured first: Npgsql then matched **no row** (an update raised
  `EntityNotFoundException` for both rows, and a delete left them). PostgreSQL's bulk paths therefore lost their
  placeholders and `Prepare()`, as SQL Server's did in TASK-854. The `Kind` is now stripped by `ConvertParameterValue`.
- MySQL keeps `Prepare()`: with `AddParameter` after it, the contract passes.
- Docs updated: the PostgreSQL connector's doc and `CLAUDE.md`, and the `UtcDateTimeBindingLiveTests` doc and assertion
  message. A follow-up is appended to the TASK-256 rule in `CLAUDE-conventions.md`.

**After the fix:**

- The contract passes 4/4 on all four providers.
- The provider suites pass, live: SQLite 3194, MySQL 2631, PostgreSQL 2609, SQL Server 2665.
- Every other test project that imports these providers passes, live (TimescaleDB on 5433). Among them: SQL.Views 4051,
  Migrations.SQL 267, TimescaleDB 251 and Localization.SQL 148.
- In StoreFactory, the 4 MongoDB / Elasticsearch / Cosmos DB / RavenDB live tests failed because those servers were not
  running; its 22 other tests pass.

## Human test plan

N/A — fully covered by automated tests.
