---
id: TASK-610
parent: EPIC-014
feature: null
status: todo
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

- [ ] On PostgreSQL, a declared column is present only under the name the connector actually emits (folded lower
      case for a bare identifier), compared exactly. A stored column differing only in case is reported. Either it
      is `Missing` plus `Unexpected`, or a new `ColumnDriftKind` (e.g. `CaseMismatch`) carrying both names.
- [ ] `EnsureColumns` never adds a second column beside a case-mismatched one (that would orphan the values). It
      reports the mismatch and leaves it to the operator, as it does for `TypeMismatch`.
- [ ] SQLite, MySQL and MSSQL keep case-insensitive matching (their identifiers resolve that way as emitted).
- [ ] A live PostgreSQL test (the framework's live-provider suite): a table with a quoted PascalCase column is
      reported. Red before.

## Out of scope

- Symbio's own boot check (Symbio TASK-931, done there).

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
