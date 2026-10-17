---
id: TASK-656
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
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

- [x] Root-caused on SQL Server (column, parameter, or read)
- [x] A `DateTime` round-trips at microsecond precision at least on MySQL and SQL Server, measured live
- [x] Existing tables: what happens to a `DATETIME` column created before the change (schema drift reports it?
      `PlanRepair` offers the ALTER?) is decided and stated
- [x] Red on the current code; the spec `docs/specs/schema-index-and-ddl.md` updated

## Out of scope

- `DateTime.Kind` coming back `Unspecified` from the SQL providers: the `[UtcField]` contract (TASK-263) owns how a
  UTC value is read back.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-656` — leave empty until then._

## Resolution (2026-10-17)

**Reproduced live, 4 red on HEAD** (`Task656DateTimePrecisionLiveTests` in the MySQL and SQL Server suites).
- MySQL read `…56.1234560` back as `…56.0000000`.
- SQL Server read `…56.1234567` back as `…56.1233333`.
- On both, a filter on the exact value matched nothing, and two writes 0.4 ms apart read back equal.

**SQL Server, root-caused on the write side, the parameter.** `sqlcmd` shows the column is `datetime2(7)`, but the
stored value is `2026-10-17 12:34:56.1233333`, which is `datetime`'s 3.33 ms grid. `MSSqlConnector.AddParameter` used
`AddWithValue`, which infers `SqlDbType.DateTime` for a `DateTime` (rule 28: the inferring producer fails quietly).
- Fix: a `DateTime` parameter is bound as `SqlDbType.DateTime2`. Against a legacy `datetime` column the server converts
  it as before.

**MySQL:** the type map gave `DateTime`, `DateTime2` and `DateTimeOffset` a bare `DATETIME`.
- Fix: `DATETIME(6)`, microseconds, MySQL's finest. The 7th digit (100 ns) is beyond any MySQL type, so the test value
  has a 0 there.
- `MySQLConnectorTests.ConvertType_MapsTypes` pinned `DATETIME`. Its two rows now expect `DATETIME(6)`, updated rather
  than deleted.

**Existing tables, decided and pinned by a live test:**
- MySQL: a column created as `DATETIME` is a `TypeMismatch` (`declared DATETIME(6), stored datetime`).
  `PlanRepair` lists it as not actioned ("a type change can lose data or fail"), as every type change since TASK-633.
  This widening is lossless, and the spec gives the operator the `ALTER TABLE … MODIFY <column> DATETIME(6)`.
  Teaching `PlanRepair` about known-lossless widenings would change TASK-633's contract, so it is not done here.
- SQL Server needs nothing for existing tables: the fix is the parameter.

**Elsewhere:**
- PostgreSQL (µs) and SQLite (every tick) are unchanged.
- `Kind` stays the `[UtcField]` contract's.
- A `DateTimeOffset` property is not a mapped SQL field type (the mapper throws `FieldAttributeException`), so the test
  covers `DateTime` only.
- Spec `schema-index-and-ddl.md`: new requirement "A DateTime keeps its sub-second digits on every SQL provider".

**Caught by the regression:** the SQL migrations store declares `AppliedAt … DEFAULT CURRENT_TIMESTAMP`, and MySQL
refuses that default on a `DATETIME(6)` column ("Invalid default value for 'AppliedAt'"): 2 red in
`MySqlMigrationsTableDialectTests`. The expression now has one producer on the connector,
`AbstractConnectorBase.CurrentTimestampDefault`: `CURRENT_TIMESTAMP` by default, `CURRENT_TIMESTAMP(6)` on MySQL.
`Migrations.SQL` passes live on all four providers (226).

Regression: all 32 importers of the `Birko.Data.SQL`, `Migrations.SQL`, MySQL and SQL Server projitems pass live, 2828 tests.
