---
id: TASK-246
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-245, TASK-247, TASK-204]
findings: []
pr: ""
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.SQL]
---

# A migration's `.Unique()` silently builds a NON-unique index on every SQL provider

## Context

Spawned from [[TASK-245]]'s planning grill, found while auditing every emitter of index DDL. Not
MySQL-specific and not covered by any of TASK-245's acceptance criteria, so it is its own task rather
than a widening of that one.

`SqlIndexBuilder.Build()` (`Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:311-345`) has two
paths. The **connector path**, taken whenever a connector was supplied:

```csharp
var indexDef = new Birko.Data.SQL.Tables.IndexDefinition
{
    Name = _indexName            // <-- Unique = _unique is NEVER set
};
indexDef.Columns.AddRange(_fields.Select((f, i) => new Birko.Data.SQL.Tables.IndexColumn
{
    ColumnName = f.Name, Order = i, IsDescending = f.Descending
}));
_connector.CreateIndexes(_collectionName, new[] { indexDef });
```

`IndexDefinition.Unique` defaults to `false` (pinned by
`Birko.Data.SQL.Tests/IndexManagement/CompositeUniqueIndexTests.cs`
`IndexDefinition_UniqueDefaultsFalse`), and `AbstractConnectorBase.CreateIndexSql` emits `UNIQUE` only
when `index.Unique` is true. So:

```csharp
migration.CreateIndex("Orders", "ux_order_docnum")
         .WithField("TenantGuid").WithField("Number")
         .Unique()                 // <-- recorded in _unique, then dropped on the floor
         .Build();
```

emits a **plain `CREATE INDEX`** on SQLite, PostgreSQL, MySQL and MSSql alike. The declared uniqueness
is silently absent — this is a missing **constraint**, not a missing optimisation, so duplicate rows
that the migration was written to forbid are accepted from that point on.

**What hides it:** the raw-SQL fallback immediately below (`:333-337`, taken only when
`_connector == null`) *does* honour `_unique` — `var uniqueStr = _unique ? "UNIQUE " : "";`. So the
feature demonstrably works in the path nobody uses in production and fails in the path everybody uses.
`Unique()` itself is not dead code either: it sets the field and returns `this`, so nothing about the
call site looks suspicious.

**Why no test caught it:** nothing in `Framework.Tests` calls `CreateIndexes` directly at all (grepped),
and the migration index path has no end-to-end coverage that inspects the created index's uniqueness.
The existing unique-index tests exercise the *attribute* declaration path
(`[IndexedField(..., IsUnique: true)]` / `[CompositeIndex(..., IsUnique = true)]`), which populates
`IndexDefinition.Unique` correctly and never goes through `SqlIndexBuilder`.

## Acceptance criteria

- [ ] `SqlIndexBuilder.Build()` propagates `_unique` to `IndexDefinition.Unique` on the connector path.
- [ ] An end-to-end test on SQLite that a migration-declared `.Unique()` index **enforces** the
      constraint — insert the duplicate and expect the write to fail. Asserting `UNIQUE` appears in the
      emitted SQL is the weaker companion assertion, not the primary one: the constraint is the point.
- [ ] The same enforcement assertion against a live provider (PostgreSQL or MySQL, gated like the
      existing `*LiveTests`), because a silently non-unique index is a data-integrity defect and the
      SQLite-only suites are what let this ship.
- [ ] Prove the test can fail: revert the one-line propagation and record the count.
- [ ] A test that a migration index declared **without** `.Unique()` is still non-unique — the guard
      against "fix" by making everything unique.
- [ ] Check the descending flag and the field order survive the same hand-off; they are set in the same
      object initialiser and neither has a test.

## Out of scope

- The raw-SQL fallback's own broken DDL (`CREATE … INDEX IF NOT EXISTS` with quoted columns, and
  `DROP INDEX IF EXISTS … ON …`) — that is [[TASK-247]].
- The `IF NOT EXISTS` / identifier-quoting fixes in the connector emitters — [[TASK-245]].
- `IIndexBuilder.Sparse()` and `WithProperty()` are `=> this` no-ops on this builder. Whether a SQL
  backend should refuse rather than silently ignore them is § SH-H037's question and a separate task if
  it is worth one; note it, do not fix it here.
