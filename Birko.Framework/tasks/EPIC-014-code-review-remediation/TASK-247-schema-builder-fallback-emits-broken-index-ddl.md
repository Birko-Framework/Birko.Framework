---
id: TASK-247
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: unassigned
created: 2026-08-18
depends-on: [TASK-245]
blocks: []
related: [TASK-245, TASK-246, TASK-209, TASK-211]
findings: []
pr: ""
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.SQL]
---

# `SqlSchemaBuilder`'s raw-SQL fallbacks emit index DDL that two providers reject

## Context

Spawned from [[TASK-245]]'s planning grill. `SqlSchemaBuilder` carries hand-written SQL for the case
where no connector was supplied (`_connector == null`), and those fallbacks are the **third and fourth
copies** of the exact clause TASK-245 is fixing in the connector emitters — written independently, so
they were never fixed alongside it.

**Fallback 1 — `SqlIndexBuilder.Build()` (`SqlSchemaBuilder.cs:333-337`):**

```csharp
var columns = _fields.Select(f => f.Descending ? $"\"{f.Name}\" DESC" : $"\"{f.Name}\" ASC");
var uniqueStr = _unique ? "UNIQUE " : "";
var sql = $"CREATE {uniqueStr}INDEX IF NOT EXISTS \"{_indexName}\" ON \"{_collectionName}\" ({string.Join(", ", columns)})";
```

Two independent defects in one statement, both measured during TASK-245's grill:

- `IF NOT EXISTS` on `CREATE INDEX` is **ERROR 1064** on MySQL 8.4 — a syntax error, so the statement
  never runs.
- The **quoted column identifiers** cannot resolve on PostgreSQL 16: `CreateTable` emits column
  definitions bare, so they are stored case-folded, and `"Status"` produces
  `ERROR: column "Status" does not exist`. Same seventh-instance identifier defect TASK-245 fixes in the
  base emitter. (`ASC` is also written explicitly here where every other emitter omits it — harmless,
  but it is a fourth divergence in one line.)

**Fallback 2 — `SqlSchemaBuilder.DropIndex()` (`SqlSchemaBuilder.cs:77`):**

```csharp
Execute($"DROP INDEX IF EXISTS {QuoteIdentifier(indexName)} ON {QuoteIdentifier(collectionName)}");
```

Wrong on **both** of the two providers, in opposite directions:

- MySQL rejects `IF EXISTS` on `DROP INDEX` — measured **ERROR 1064**; MySQL *requires* the `ON <table>`
  this line has.
- PostgreSQL accepts `IF EXISTS` but its `DROP INDEX` takes **no `ON` clause at all**, so the trailing
  `ON "T"` is a syntax error there.

So the statement cannot be correct for both as written; it needs the same per-provider emitter the
connector path has (`AbstractConnectorBase.DropIndexSql` + the MySQL override TASK-245 adds).

## Why this is lower priority than its siblings

Both fallbacks are reachable **only** when `SqlSchemaBuilder` was constructed without a connector, and
every in-tree construction supplies one. So this is latent rather than shipping — unlike [[TASK-246]],
which fires on the path everybody uses. It is filed because a hand-written duplicate of a statement the
framework already knows how to emit is how this family keeps rediscovering the same defect, and because
the fallback is by definition the path taken when the normal one is unavailable, i.e. under a condition
nobody is watching.

## Acceptance criteria

- [ ] Decide and record whether the fallbacks should exist at all. Preferred: **delete them** and require
      a connector (throw with a message naming what to pass), since the connector emitters are now correct
      on all four providers and a second implementation is exactly the duplication § Conventions' "one
      producer" rule exists to remove. If they must stay, they route through
      `AbstractConnectorBase.CreateIndexSql` / `DropIndexSql` rather than re-deriving the SQL.
- [ ] If deleted: a test that constructing `SqlSchemaBuilder` without a connector fails fast, with the
      message naming the fix — § SH-H037 requires the opt-out to exist and be checked, so verify the
      caller has a usable door.
- [ ] If retained: gated MySQL **and** PostgreSQL tests that a fallback-path `CreateIndex` and
      `DropIndex` actually create and drop the index — asserted against `information_schema.statistics` /
      `pg_indexes`, not against "nothing threw". `SqlSchemaBuilder`'s `Execute` swallow behaviour must be
      checked first: TASK-209 and TASK-211 both found that a DDL call reporting success proves nothing in
      this layer.
- [ ] Prove each test can fail by reverting to the current statement text, per provider, with counts.
- [ ] Confirm whether `_unique` needs the same treatment here once [[TASK-246]] lands — the fallback
      currently honours it while the connector path does not, so TASK-246 makes the two agree and this
      task must not silently undo that.

## Out of scope

- The connector-side `IF NOT EXISTS` and identifier fixes — [[TASK-245]] (this task depends on it, so its
  emitters are correct before this one reuses them).
- `_unique` propagation on the connector path — [[TASK-246]].
- `SqlSchemaBuilder`'s non-index fallbacks (`AddField`, table create/drop, etc.) were not audited during
  the grill. If they carry the same duplication, that is a separate survey, not an assumption to act on
  here — but say so in the close notes either way.
