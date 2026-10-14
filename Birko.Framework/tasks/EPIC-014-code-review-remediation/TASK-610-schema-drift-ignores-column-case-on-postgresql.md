---
id: TASK-610
parent: EPIC-014
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
related: [TASK-269]
findings: []
pr: null
github-issue: null
jira-key: null
---

# On PostgreSQL, DetectDrift / EnsureColumns accept a column that differs only in case, which no query can reach

## Context

Found 2026-10-14 by Symbio (its TASK-931, the same defect in Symbio's own boot drift check, fixed there in
`Symbio.DataAccess/Sql/SchemaDriftCheck.cs`). This task is the framework's copy of it.

On PostgreSQL the connector writes column names **bare**, so PostgreSQL folds them to lower case: a `SupplierVatin`
property is the column `suppliervatin`. A column created **quoted** with another case (`"SupplierVatin"`, which is what
a hand-written ledger DDL produced) is a different identifier that bare SQL never resolves:
`42703: column tenantsettings.suppliervatin does not exist`.

Verified in source 2026-10-14:
- `AbstractConnector_SchemaDrift.cs:183`: `ReadStoredColumns` keys stored columns with
  `StringComparer.OrdinalIgnoreCase`. So `DetectDrift` (TASK-269) reports neither `Missing` nor anything else for a
  case-only mismatch, and `SchemaDriftHealthCheck` stays healthy over a table every query on it fails against.
- `AbstractConnector_EnsureColumns.cs:77` matches fields to drifts the same way, so `EnsureColumns` would not add the
  column the app reads either.

Measured in Symbio, not here: with Symbio's check doing exactly this, a deploy routine judged staging clean,
production took the DDL, and `GET /api/tenant-settings` answered 500. Symbio does not call `DetectDrift` or
`EnsureColumns`, so Symbio is not exposed through this code. Any other consumer on PostgreSQL is.

## Acceptance criteria

- [x] On PostgreSQL, a declared column is present only under the name the connector actually emits (folded lower
      case for a bare identifier), compared exactly. A stored column differing only in case is reported. Either it
      is `Missing` plus `Unexpected`, or a new `ColumnDriftKind` (e.g. `CaseMismatch`) carrying both names.
- [x] `EnsureColumns` never adds a second column beside a case-mismatched one (that would orphan the values). It
      reports the mismatch and leaves it to the operator, as it does for `TypeMismatch`.
- [x] SQLite, MySQL and MSSQL keep case-insensitive matching (their identifiers resolve that way as emitted).
- [x] A live PostgreSQL test (the framework's live-provider suite): a table with a quoted PascalCase column is
      reported. Red before.

## Out of scope

- Symbio's own boot check (Symbio TASK-931, done there).
- The same class on other providers, found by the audit this task ran: a case-sensitive MSSQL collation
  (TASK-611), InfluxDB migration bucket lookups (TASK-612), and the ElasticSearch `.keyword` suffix on
  keyword-mapped fields (TASK-613). MongoDB, CosmosDB, RavenDB, JSON/XML/InMemory and the InfluxDB store
  read clean.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

1. **One producer for "the name a column is stored under".** `AbstractConnectorBase` gains
   `StoredColumnName(name)` (folded when `FoldsUnquotedIdentifiers`, else as declared) and
   `StoredColumnNameComparer` (`Ordinal` when the provider folds, `OrdinalIgnoreCase` otherwise; virtual, so
   a provider whose resolution depends on configuration can override it). `CatalogueNameLiteral` is rewritten
   on top of `StoredColumnName`, so the two cannot disagree.
2. **`DetectDrift`** keys `ReadStoredColumns` with the connector's comparer and looks each field up under
   `StoredColumnName(field.Name)`. A field not found that way but present under a case-insensitive match is
   reported as a new `ColumnDriftKind.CaseMismatch` (appended, value 3), carrying the stored spelling in a new
   `ColumnDrift.StoredColumn`; that stored column is then **not** also reported `Unexpected`.
3. **`EnsureColumns`** acts on `Missing` only, so a `CaseMismatch` is never added beside (the AC); its field
   lookup uses the declared name exactly (it is the model's own name) instead of `OrdinalIgnoreCase`.
4. `ColumnDrift.ToString()` and the kind's XML doc describe the new case; `SchemaDriftHealthCheck` already
   treats any drift as unhealthy (checked), so it needs no change.
5. **Tests.** PostgreSQL live (`SchemaDriftLiveTests`, `EnsureColumnsLiveTests`): a table with a quoted
   PascalCase column reports `CaseMismatch` and `EnsureColumns` adds nothing; proven red against the
   unfixed code. Control: the framework-created table still reports clean. SQLite end-to-end: the same
   case-only difference stays clean there (case-insensitive provider).
6. CLAUDE.md Recent Updates entry; Birko.Data.SQL CLAUDE.md if it documents the drift kinds.

## Progress log

- 2026-10-14 — Red first (live PostgreSQL 16): `IsClean` was true over a table whose `"SupplierVatin"` column fails
  every query with 42703, and the premise was asserted in the test. Fixed with `ColumnDriftKind.CaseMismatch` and
  the connector's `StoredColumnName` / `StoredColumnNameComparer`. Two existing fixtures (`PgDriftStale`) had been
  built with quoted PascalCase columns, which is the defect's own shape, and the old check passed them. They now use
  bare columns. PostgreSQL 147/147 live, SQLite 503/503 (with a new case-insensitive control), SQL core 710/710,
  Health.SQL 20/20.
