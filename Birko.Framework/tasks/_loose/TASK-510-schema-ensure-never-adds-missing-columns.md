---
id: TASK-510
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-03
depends-on: []
blocks: []
findings: [FIELD-019]
pr: null
github-issue: null
jira-key: null
---

# A table created before a type was mapped never gets that column — schema-ensure is create-only, and `AlterTableAdd` cannot add a NOT NULL column to a table with rows

## Context

Found in consumer DraCode (its TASK-082, fixed locally 2026-10-03). DraCode's `UsageRecordEntity.EstimatedCostUsd`
(`double`) existed from the start, but until SH-H037's fix (34928514, 2026-08-08) `double` mapped to no column,
so `CREATE TABLE` silently left it out. After the mapping landed, every INSERT named a column the table did not
have; the consumer swallowed the exception, and two months of usage records were lost.

Two framework gaps made this possible, and any consumer whose database predates a mapping fix has them:

1. **Schema-ensure is create-only.** `CreateSchemaAsync` / store init runs `CREATE TABLE IF NOT EXISTS` and never
   adds a column the model declares and the table lacks. `DetectDrift` (TASK-269) *reports* exactly this as
   `ColumnDriftKind.Missing`, but is deliberately on-demand only, and nothing acts on it.
2. **`AlterTableAdd` cannot add a value-type column to a populated table on SQLite.** The SQLite
   `FieldDefinition` renders `NOT NULL` with no `DEFAULT`, and SQLite refuses
   `ADD COLUMN … NOT NULL` without a default on a table that has rows ("Cannot add a NOT NULL column with default
   value NULL"). So even a consumer that detects the gap cannot use the framework to close it.

DraCode's workaround (`SqlUsageRepository.AddMissingColumnsAsync`): run `DetectDrift`, then
`ALTER TABLE "<t>" ADD COLUMN "<c>" <declared> DEFAULT 0|''` for each `Missing` column. That should become one framework
call.

### Found while triaging (2026-10-03)

3. **`AlterTableAdd` cannot add a column on SQL Server at all.** Both `AlterTableAdd` and `AlterTableAddAsync`
   hard-code `ALTER TABLE … ADD COLUMN …` in `Birko.Data.SQL`, and no provider overrides them; T-SQL has no
   `COLUMN` keyword there. Measured on 2022: `Msg 156, Incorrect syntax near the keyword 'COLUMN'`. So
   `SqlSchemaBuilder.AddColumn` (migrations) has never worked against MSSQL either. The sync string overload is
   also not `virtual`, so a provider could not have fixed it.
4. **`FieldDefinition` is the CREATE TABLE definition, constraints included.** It can emit `PRIMARY KEY`,
   `UNIQUE`, `IDENTITY` / `AUTOINCREMENT`. SQLite refuses `ADD COLUMN` with `PRIMARY KEY` / `UNIQUE`, and on any
   provider a unique column filled with one default on a populated table violates itself. Ensure-columns must
   **refuse** such a column, before any DDL, not try it.
5. **One default value, not a second type renderer.** The value an old row reads back must be what the model
   would hold had it never been assigned (`default(T)`), in the shape the field *writes* — `TimeOnly` is stored as
   `'00:00:00'` text, not `''`, so a DbType-keyed literal table would be wrong. The field's writer produces the
   value; the connector only renders it as a literal. Measured literal constraints:
   - PostgreSQL: `DEFAULT 0` on `BOOLEAN` is refused ("default expression is of type integer") — needs `FALSE`
   - MySQL 8.4: a plain `DEFAULT` on `LONGTEXT` is refused (ERROR 1101); the expression form `DEFAULT ('…')` works
     on every type tried (LONGTEXT, DATETIME `0001-01-01`, CHAR(36), TINYINT(1), DECIMAL) under strict mode
   - SQL Server: `ADD b INT NOT NULL DEFAULT 0, … DATETIMEOFFSET … DEFAULT '0001-01-01 00:00:00+00:00'` fills the
     existing row

## Acceptance criteria

- [x] An opt-in, additive "ensure columns" on SQL stores/connectors: adds every `Missing` column `DetectDrift` reports; never drops or retypes anything; idempotent
  — `AbstractConnector.EnsureColumns(Type)`, reached from a store as `store.Connector.EnsureColumns(typeof(T))`
- [x] `AlterTableAdd` (or the new path) gives a NOT NULL value-type column a type default (`0`, `''`, …) so it succeeds on a populated table, on every provider that needs it (at least SQLite; check PostgreSQL/MSSQL/MySQL behaviour and state it)
  — all four need it (SQLite refuses outright; PostgreSQL and SQL Server refuse because rows would violate NOT NULL;
  MySQL would fill an implicit default, not necessarily `default(T)`), so it is in `AddColumnDefinition` for every
  provider. The default is `default(T)` in stored form, not `''` — a `[RequiredField]` string is refused instead.
- [x] Tests per provider available locally: an old-shape table with rows gains the column, existing rows read back with the default, a second run changes nothing
  — SQLite 8/8 (`EnsureColumnsEndToEndTests`), PostgreSQL 16 / MySQL 8.4 / SQL Server 2022 3/3 each
  (`EnsureColumnsLiveTests`), 16 column types. Mutation: dropping the `DEFAULT` fails 3 of 8 SQLite tests.
- [x] Documented where schema-ensure is described, including that it stays opt-in (TASK-204/254: a diagnostic must not stop a store starting)
  — `Birko.Data.SQL/CLAUDE.md` § Adding columns a table predates, README § Adding Missing Columns, Recent Updates

## Out of scope

- Type drift (`ColumnDriftKind` other than `Missing`) — reported by `DetectDrift`, not auto-fixed here
- TASK-150 (the remaining unmapped types) — separate; this task is about tables that predate a mapping, whatever the type
- Removing DraCode's local helper — done in DraCode once this ships

## Human test plan

N/A — the upgrade path is asserted per provider by automated tests against real database files/instances.

## Implementation plan

1. **Value producer on the field.** Split `AbstractField.Write(entity)` into `Write` → `ToStorage(raw)`; move
   the three `Write` overrides (`IntegerField` enum→int, `TimeOnlyField`, `UtcDateTimeField`) onto `ToStorage`.
   Add `AbstractField.DefaultStoredValue` = `ToStorage(default of the underlying CLR type)`, null for a reference
   type. One producer for "what this column holds for an unassigned property".
2. **Literal renderer on the connector.** `AbstractConnectorBase.DefaultValueLiteral(object)` renders
   numbers (invariant), bool, string (escaped via `SqlLiteral`), Guid, DateTime, DateTimeOffset; anything else
   throws. Provider hooks only where measured: PostgreSQL `TRUE`/`FALSE`, MySQL `DEFAULT (expr)` form and
   offset-less DateTimeOffset.
3. **`AlterTableAdd`.** `virtual AddColumnClause` (`ADD COLUMN`; MSSQL `ADD`). One column definition producer
   `AddColumnDefinition(field)` = `FieldDefinition(field)` + a `DEFAULT` when the field is NOT NULL, not
   identity/primary, and has a default value. Used by sync and async.
4. **`EnsureColumns(Type)` / `EnsureColumnsAsync(Type)`** on the connector: `DetectDrift`; unsupported report →
   `NotSupportedException`; table absent → nothing (CREATE TABLE will make it whole); collect `Missing` only;
   refuse up front (before any DDL) a column that is primary, unique, autoincrement, or NOT NULL with no
   default (a `[Required]` string/binary — the framework will not invent a value the model says must be
   supplied); then add each. Returns the drifts it closed. Never called from init (rule 49/50: explicit call
   throws).
5. **Tests.** Offline (SQLite, `Birko.Data.SQL.SqLite.Tests`): every value type round-trips `default(T)` on a
   pre-existing row; second run adds nothing; refusals; nothing else touched. Live per provider
   (PostgreSQL/MySQL/MSSQL suites beside `SchemaDriftLiveTests`): old-shape table with a row, gains columns, row
   reads back defaults, report clean afterwards, second run is a no-op. Plus the MSSQL `AlterTableAdd` syntax.
6. **Docs.** Birko.Data.SQL CLAUDE.md/README (schema-ensure, drift), CLAUDE.md Recent Updates, CHANGELOG.
