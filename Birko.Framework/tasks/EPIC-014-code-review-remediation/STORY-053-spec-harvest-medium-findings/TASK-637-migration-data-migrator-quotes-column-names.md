---
id: TASK-637
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The migration data migrator and `RenameField` quote column names, so on PostgreSQL they miss a PascalCase model's columns

## Context

Found while testing [[TASK-272]] (2026-10-16), measured live on PostgreSQL 16. Rule 17: quote table identifiers, never
column identifiers. The framework creates columns bare, so PostgreSQL folds `Name` to `name`. Three migration sinks quote
the column instead:

- `SqlDataMigrator.BulkInsert` (`Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs`, column list):
  `42703: column "Guid" of relation "T272Evts" does not exist`.
- `SqlDataMigrator.UpdateDocuments` SET clauses, and the JSON filter's columns (`MigrationFilterTerms`):
  `DeleteDocuments(…, {"Name":"n"})` gave `42703: column "Name" does not exist`.
- `SqlSchemaBuilder.RenameField` (through `RenameColumnSql`, which quotes `from` and `to`): measured only as a
  consequence. Renaming `Qty` needs `qty` on PostgreSQL, then the renamed column is created quoted and case-preserved.

A migration written against the model's own property names fails on PostgreSQL. On SQL Server, MySQL and SQLite it works,
because their identifiers are case-insensitive. TASK-272's test uses folded names to stay on its own subject.

## What to decide

These are caller strings (from a migration's code or JSON), so emitting them bare needs a containment story (rule 33:
validate a bare identifier, or refuse). Options:
1. Resolve the name against the table's metadata when the collection is a mapped entity (rule 15), else validate and emit
   bare.
2. Fold the name on a provider that folds (`CatalogueNameLiteral` / `StoredColumnName`'s logic), keeping the quotes.

Measure what `RenameColumnSql` must emit on each provider for a model-created column before choosing.

## Acceptance criteria

- [ ] A decision recorded, with reasons
- [ ] `BulkInsert`, `UpdateDocuments`, `DeleteDocuments` and `CountDocuments` with a JSON filter, and `RenameField`, work
      on PostgreSQL with a PascalCase model's property names; measured live on all four providers
- [ ] A caller string that is not a plain identifier is refused, not interpolated
- [ ] Red on the current code; the TASK-272 test switched back to the model's names

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-637` — leave empty until then._
