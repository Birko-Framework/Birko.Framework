---
id: TASK-656
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-17
depends-on: []
blocks: []
findings: [FIELD-047]
pr: null
github-issue: null
jira-key: null
---

# MySQL stores a DateTime to the second, and SQL Server to about 3 ms

## Context

Found 2026-10-17 while measuring [[TASK-647]]. A temporary probe wrote `UpdatedAt = 2026-10-17T12:34:56.1234567Z`
through `AsyncDataBaseBulkStore<DB, T>` and read it back by Guid (live, local Docker):

| Provider | Read back | Lost |
|---|---|---|
| SQLite | `…56.1234567` | Kind only |
| PostgreSQL | `…56.1234560` | Kind; the 7th digit (the type's µs precision, expected) |
| **MySQL** | `…56.0000000` | Kind, and **every sub-second digit** |
| **SQL Server** | `…56.1233333` | Kind, and precision down to `datetime`'s ~3.33 ms |
| MongoDB | `…56.1230000Z` | ms (BSON date, expected) |
| ElasticSearch, RavenDB | exact | — |

- **MySQL:** `MySQLConnector`'s type map gives `DbType.DateTime` / `DateTime2` / `DateTimeOffset` the column type
  `DATETIME`, which has no fractional digits (`Birko.Data.SQL.MySQL/Database/Connectors/MySQLConnector.cs:278-282`).
  `DATETIME(6)` keeps microseconds.
- **SQL Server:** the column is already `DATETIME2` (`MSSqlConnector.cs:333`, CR-H086), yet the value lost
  precision, so the loss is probably on the write side: a parameter inferred or bound as `datetime` (rule 28).
  **Measured, not root-caused.**

Every model is affected, not only sync: two writes within a second on MySQL read back equal, and an
`OrderBy(UpdatedAt)` ties. [[TASK-647]] made sync immune by versioning each side as it is stored; this is the data
itself.

## Acceptance criteria

- [ ] Root-caused on SQL Server (column, parameter, or read)
- [ ] A `DateTime` round-trips at microsecond precision at least on MySQL and SQL Server, measured live
- [ ] Existing tables: what happens to a `DATETIME` column created before the change (schema drift reports it?
      `PlanRepair` offers the ALTER?) is decided and stated
- [ ] Red on the current code; the spec `docs/specs/schema-index-and-ddl.md` updated

## Out of scope

- `DateTime.Kind` coming back `Unspecified` from the SQL providers: the `[UtcField]` contract (TASK-263) owns how a
  UTC value is read back.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-656` — leave empty until then._
