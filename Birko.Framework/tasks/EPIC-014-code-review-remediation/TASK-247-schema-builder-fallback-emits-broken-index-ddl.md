---
id: TASK-247
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: unassigned
created: 2026-08-18
depends-on: [TASK-245]
blocks: []
related: [TASK-245, TASK-246, TASK-209, TASK-211]
findings: []
pr: "Birko.Data.Migrations.SQL 14896a0"
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

- [x] Decide and record whether the fallbacks should exist at all. Preferred: **delete them** and require
      a connector (throw with a message naming what to pass), since the connector emitters are now correct
      on all four providers and a second implementation is exactly the duplication § Conventions' "one
      producer" rule exists to remove. If they must stay, they route through
      `AbstractConnectorBase.CreateIndexSql` / `DropIndexSql` rather than re-deriving the SQL.
- [x] If deleted: a test that constructing `SqlSchemaBuilder` without a connector fails fast, with the
      message naming the fix — § SH-H037 requires the opt-out to exist and be checked, so verify the
      caller has a usable door.
- [x] If retained: gated MySQL **and** PostgreSQL tests that a fallback-path `CreateIndex` and
      `DropIndex` actually create and drop the index — asserted against `information_schema.statistics` /
      `pg_indexes`, not against "nothing threw". `SqlSchemaBuilder`'s `Execute` swallow behaviour must be
      checked first: TASK-209 and TASK-211 both found that a DDL call reporting success proves nothing in
      this layer.
- [x] Prove each test can fail by reverting to the current statement text, per provider, with counts.
- [x] Confirm whether `_unique` needs the same treatment here once [[TASK-246]] lands — the fallback
      currently honours it while the connector path does not, so TASK-246 makes the two agree and this
      task must not silently undo that.

## Out of scope

- The connector-side `IF NOT EXISTS` and identifier fixes — [[TASK-245]] (this task depends on it, so its
  emitters are correct before this one reuses them).
- `_unique` propagation on the connector path — [[TASK-246]].
- `SqlSchemaBuilder`'s non-index fallbacks (`AddField`, table create/drop, etc.) were not audited during
  the grill. If they carry the same duplication, that is a separate survey, not an assumption to act on
  here — but say so in the close notes either way.

## Human test plan

**N/A — covered by automated tests.** The change is a deletion plus two refusals; both are asserted, and the
whole-solution build plus 17 suites cover the removal not having broken a caller.

## Outcome — DECISION: delete the fallbacks, require the connector

### The decision the first criterion demanded, and the evidence for it

**Deleted, all eight of them** — not just the two index ones this task was filed about. `SqlSchemaBuilder`'s
optional connector was the switch; every method carried a hand-written raw-SQL branch for the null case.

Two of those branches had drifted into being **wrong on two providers**:

| statement | MySQL | PostgreSQL |
|---|---|---|
| `CREATE {UNIQUE }INDEX IF NOT EXISTS "ix" ON "T" ("Col" ASC)` | rejects `IF NOT EXISTS` (1064) | cannot resolve a quoted column against the folded one bare-column DDL stores (42703) |
| `DROP INDEX IF EXISTS "ix" ON "T"` | rejects `IF EXISTS`, requires the `ON` | accepts `IF EXISTS`, permits no `ON` |

So the "connector-free" path was not portability, only the appearance of it: it emitted DDL half the supported
providers reject. Repairing it would have kept a second implementation of statements the connectors already
emit correctly per dialect — the duplication § Conventions' one-producer rule exists to remove.

### Reachability, measured before deleting

- Only production construction: `SqlMigrationRunner.cs:152` → `SqlMigrationContext`, and the runner's own
  constructor does `_connector = connector ?? throw new ArgumentNullException`. So the optional 4th argument of
  `SqlMigrationContext` was the **sole door** to the fallbacks.
- **All 16 consumer repos swept:** 4 import `Birko.Data.Migrations.SQL`; **0** hand-build a
  `SqlMigrationContext` / `SqlSchemaBuilder` / `SqlDataMigrator`; **0** use `ISchemaBuilder` at all; **0**
  declare an index through a migration. The two real users (Symbio's `ProviderMigrationRunnerFactory`, Reps'
  `RepsMigrator`) both go through the runner with a real connector.

That sweep also **corrected [[TASK-246]]** from *shipping* to *latent* — see the correction note in that task.
A claim about live impact had already gone into a commit message, so it is recorded rather than softened.

### What the fallback was actually costing

**Not runtime behaviour — test integrity.** `connector == null` was how **every** test in
`Birko.Data.Migrations.SQL.Tests` constructed the builder, so six tests exercised only the dead branch. That is
precisely how TASK-246's missing `Unique` flag on the *live* branch stayed green. Requiring the connector
converted those six into real tests. The generalised rule is now in § Conventions: **a dependency that can be
null silently selects a different implementation, and a test that passes null may be asserting about code
nothing ships.**

### Verification

Whole-solution build: **0 errors**. **1,199 tests green across 17 suites** (`Birko.Data.Migrations.SQL.Tests`
47, `Birko.Data.SQL.View.Migrations.Tests` 14, plus the SQL/provider/view/jobs/workflow suites). `SqlSchemaBuilder`
went 369 → 321 lines.

| revert | result | proves |
|---|---|---|
| **P** schema builder accepts null again | **2 of 47** fail | both refusals, and that the message assertions bite |

There is deliberately no "revert the deleted fallback" case: the fallbacks were unreachable, so restoring them
fails nothing — which *is* the argument for deleting them. The meaningful proof is that the refusal is
load-bearing and that its message names the door (§ SH-H037).

### Criteria that needed answers rather than code

- **`_unique` treatment (last criterion):** confirmed. TASK-246 made the connector path honour it; deleting the
  fallback leaves exactly one path, which honours it, and `MigrationUniqueIndexTests` still proves it. Nothing
  silently undid that fix — and the test that used to pin the *fallback's* correctness was rewritten into the
  refusal test rather than deleted.
- **The unaudited non-index fallbacks (out-of-scope note):** audited here, since deleting the switch removes
  them too. `DropCollection`, `AddField`, `DropField`, `QuoteIdentifier`, `CollectionBuilder.Build` all had raw
  branches; each connector counterpart uses the provider's own emitter, so all five were strictly worse copies.
  `FieldTypeToSql`, `FormatValue` and `FormatColumn` were their only callers and are gone with them.

### Two capabilities that genuinely disappear, written down

- **Composite primary keys.** The deleted `CollectionBuilder` fallback emitted `PRIMARY KEY (a, b)` from
  `_primaryKeyFields`; `AbstractConnector.CreateTable` renders `PRIMARY KEY` per column from each field's flag
  instead. Nothing in the tree or any consumer declares one through this builder, so nothing in use was lost —
  but a deletion that quietly drops a capability is indistinguishable later from one that never had it.
- **`RenameField` keeps hand-written SQL**, because there is no connector equivalent. It now quotes through the
  connector's dialect rather than a hardcoded `"`, and `RENAME COLUMN` is not universal (MySQL 8.0+; older
  needs `CHANGE`) — a latent per-provider gap of exactly the family this epic keeps closing, recorded because
  nothing calls it.
