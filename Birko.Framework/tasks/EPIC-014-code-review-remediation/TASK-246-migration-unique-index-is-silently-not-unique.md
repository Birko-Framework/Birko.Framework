---
id: TASK-246
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-245, TASK-247, TASK-204]
findings: []
pr: "Birko.Data.Migrations.SQL 115abd4"
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

- [x] `SqlIndexBuilder.Build()` propagates `_unique` to `IndexDefinition.Unique` on the connector path.
- [x] An end-to-end test on SQLite that a migration-declared `.Unique()` index **enforces** the
      constraint — insert the duplicate and expect the write to fail. Asserting `UNIQUE` appears in the
      emitted SQL is the weaker companion assertion, not the primary one: the constraint is the point.
- [x] The same enforcement assertion against a live provider (PostgreSQL or MySQL, gated like the
      existing `*LiveTests`), because a silently non-unique index is a data-integrity defect and the
      SQLite-only suites are what let this ship.
- [x] Prove the test can fail: revert the one-line propagation and record the count.
- [x] A test that a migration index declared **without** `.Unique()` is still non-unique — the guard
      against "fix" by making everything unique.
- [x] Check the descending flag and the field order survive the same hand-off; they are set in the same
      object initialiser and neither has a test.

## Out of scope

- The raw-SQL fallback's own broken DDL (`CREATE … INDEX IF NOT EXISTS` with quoted columns, and
  `DROP INDEX IF EXISTS … ON …`) — that is [[TASK-247]].
- The `IF NOT EXISTS` / identifier-quoting fixes in the connector emitters — [[TASK-245]].
- `IIndexBuilder.Sparse()` and `WithProperty()` are `=> this` no-ops on this builder. Whether a SQL
  backend should refuse rather than silently ignore them is § SH-H037's question and a separate task if
  it is worth one; note it, do not fix it here.

## Human test plan

**N/A — covered by automated tests.** The claim is "the engine refuses the duplicate", which the engine
either does or does not; nothing a human adds by looking. Verified on SQLite and live PostgreSQL 16.

## Outcome

**One line missing from an object initialiser, on all four providers.** `SqlIndexBuilder.Build()`'s connector
path — the branch every production migration takes — built its `Tables.IndexDefinition` without
`Unique = _unique`. `IndexDefinition.Unique` defaults to `false` and `CreateIndexSql` emits `UNIQUE` only when
it is true, so a migration's `.Unique()` produced a plain index and the declared **constraint** simply did not
exist.

### What hid it — the part worth carrying

`SqlSchemaBuilder`'s builders have **two branches**: the connector path, and a raw-SQL fallback taken only
when `connector == null`. **The fallback honoured `_unique` correctly**, and every pre-existing test in
`Birko.Data.Migrations.SQL.Tests` constructed the builder as `new SqlSchemaBuilder(conn, null, null)` — so
the suite exercised only the branch nobody uses in production, and was green throughout. A test that supplies
`null` for the connector is testing the fallback, whatever it looks like it is testing.

That is now the standing rule in § Conventions: **where a component has a fallback branch, a test that takes
the fallback is not a test of the component** — check which branch your fixture selects, and prefer a revert
to a reading.

### Verification

**Test-first: 2 of 5 new SQLite tests failed against the unfixed builder** before the production line changed
— precisely the two asserting uniqueness. The other three are "must not change" pins that correctly pass
either way (no-`.Unique()` stays non-unique; column order and `DESC` survive; the fallback stays correct).

| suite | result |
|---|---|
| `Birko.Data.Migrations.SQL.Tests` | **46** green (39 + 7 new), live PostgreSQL 16 |
| `Birko.Data.SQL.Tests` | 543 green |
| `Birko.Data.SQL.SqLite.Tests` | 220 green |
| `Birko.Data.SQL.PostgreSQL.Tests` | 61 green |

**Revert (drop `Unique = _unique`): 3 of 46 fail** —
`A_migration_declared_unique_index_enforces_the_constraint` (SQLite),
`The_emitted_ddl_records_the_index_as_unique` (SQLite), and
`A_migration_declared_unique_index_is_enforced_on_postgresql` (live). The live one failing is the point of
including it: this is a data-integrity defect and a SQLite-only suite is what let it ship.

**The primary assertion is enforcement, never emitted text.** Asserting `UNIQUE` appears in `sqlite_master`
would also pass for an index the engine never applied, so each test inserts the duplicate and requires the
write to fail — then checks the catalogue as the weaker companion.

### Notes

- `Birko.Data.Migrations.SQL.Tests` gained an import of `Birko.Data.SQL.PostgreSQL.projitems` so the live half
  could exist beside the code under test. Gated on `BIRKO_PG_HOST`; the suite skips cleanly without it.
- PostgreSQL was chosen over MySQL for the live half because it case-folds unquoted identifiers, so the test
  doubles as end-to-end proof that the index binds to the columns it names (TASK-245's identifier fix) rather
  than being created against something that does not resolve.
- **Second instance of the lost-flag shape in two days.** `SqlIndexManager.ToSqlIndexDefinition` dropped the
  identical property one layer over (TASK-245). Both were invisible because an omission in an object
  initialiser looks like nothing at all.
- Out of scope and still open: the fallback's own broken DDL ([[TASK-247]]), whose first question is now
  sharper — the connector emitters are correct on every provider, so should the fallback exist at all?
