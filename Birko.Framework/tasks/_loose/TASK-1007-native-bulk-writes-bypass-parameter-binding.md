---
id: TASK-1007
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Red first, on each of the three providers, live on Docker: a `BulkUpdate` and a composite-key `BulkDelete` whose
  values depend on the framework's conversion. At least:
  - a `DateTime` with seven fractional digits;
  - a Guid key on SQLite;
  - a `DateTimeKind.Utc` value on PostgreSQL.

  Record which fail.
- [ ] Every row's SET and key values bind through `AddParameter`, as SQL Server's do.
- [ ] Green on all three, and the SQL suites pass live with `BIRKO_REQUIRE_LIVE=1`.

## Human test plan

N/A — fully covered by automated tests.
