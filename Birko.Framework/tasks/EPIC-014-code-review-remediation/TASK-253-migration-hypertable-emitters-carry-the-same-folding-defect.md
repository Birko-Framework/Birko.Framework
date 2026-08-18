---
id: TASK-253
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-209, TASK-211, TASK-245, TASK-249, TASK-472]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB, Birko.Data.TimescaleDB]
---

# The migration hypertable emitters carry the same identifier defect — and one bypasses the DDL funnel

Spawned by Symbio **TASK-472**, which fixed `TimescaleDBConnector.BuildCreateHypertableSql` after measuring
that **no hypertable had ever been created for a PascalCase-named entity** on TimescaleDB. That fix was scoped
to the store path, because that is what TASK-472 needed. The same defect exists in at least three other
emitters that the task deliberately did not touch.

## What is wrong

### 1. `Birko.Data.Migrations.TimescaleDB` duplicates the broken statement, without even the escaping

`TimescaleDBMigration` (around lines 40–56) builds the same call by raw interpolation:

```csharp
var sql = $"SELECT create_hypertable('{tableName}', '{timeColumnName}'{chunkIntervalSql});";
```

- **Bare table** → the regclass folds, so a PascalCase table raises `42P01` — the exact defect TASK-472 fixed
  one layer over. Whether it is swallowed here depends on the migration context's error handling rather than on
  `PostgreSQLConnector.OnException`, so re-measure rather than assume it is silent.
- **Unfolded column** → `42703` against the bare-emitted, case-folded stored column.
- **No quote escaping at all**, unlike the connector, which at least doubled single quotes. `tableName` and
  `timeColumnName` are free text from the migration author.

`CreateHypertableWithSpace` has the identical shape plus a third interpolated identifier
(`spaceColumnName`), and `AddCompressionPolicy` / `BuildCompressionPolicySql` take a `tableName` and an
`orderByColumn` / `segmentByColumn` the same way — **audit the whole file, not just the two methods named
here.** This is the "enumerate that sink's callers by provenance" rule from TASK-245, which TASK-249 then had
to re-apply because the first pass found only one of two caller-derived sinks.

### 2. `TimescaleDBConnector.CreateHypertableAsync` bypasses the DDL funnel

The sync `CreateHypertable` goes through `DoDdlCommand` (TASK-243's funnel, with the
`SupportsTransactionalDdl` decision inside it). The async twin instead does its own
`CreateConnection(_settings)` + `OpenAsync`, so it neither joins an ambient boundary nor is suppressed off one
— it simply opens a second connection, which on PostgreSQL is legal and therefore silent. It is reachable
public API: `AsyncTimescaleDBStore.CreateHypertableAsync`,
`AsyncTimescaleDBModelRepository.CreateHypertableAsync` and
`Birko.Data.TimescaleDB.ViewModel`'s `AsyncTimescaleDBRepository.CreateHypertableAsync` all forward to it.

Note the shape TASK-245 hit twice: **check which twin the production path actually runs before costing the
fix.** Schema-ensure reaches the *sync* emitter even from an async store (`AsyncDataBaseStore.InitCoreAsync`
calls the sync `Connector.CreateTable` inside a `Task.Run`), so the async method's only callers are the three
explicit public ones above.

## Why it was left out of TASK-472

Different repo, different reachability, and a different fix shape — the migration emitters have no connector to
delegate to, so they need either their own quoting/folding or a shared helper. Bundling them would have widened
a verification task into a multi-repo refactor. Grouped here as one task rather than three, per the aggregator's
"several small ones from the same thread → one grouped task" rule.

## Acceptance criteria

- [ ] Every `create_hypertable` / policy emitter in `Birko.Data.Migrations.TimescaleDB` quotes its table as an
      identifier inside the literal and pre-folds any column name, with single **and** double quotes escaped.
      One producer if at all possible — three copies of this statement is how the defect survived in the first
      place (TASK-245's "when you find the same statement written three times, look for what got lost upstream").
- [ ] `CreateHypertableAsync` routed through `DoDdlCommandAsync`, or an explicit written reason why it must not
      be — and if it stays outside, say what that means for a caller inside a boundary.
- [ ] Verified against **live TimescaleDB**, asserting against `timescaledb_information.hypertables` and not
      against "the call did not throw". TASK-209's lesson: this layer swallows, so a no-op is indistinguishable
      from success. `docker run -d -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=birkoview -p 5433:5432
      timescale/timescaledb:latest-pg16` is enough; TASK-472's suites gate on `BIRKO_TS_HOST`.
- [ ] Proven able to fail: revert each substitution and watch the matching test go red.
- [ ] An injection test per caller-derived sink, matching `IndexIdentifierInjectionTests` — these take free
      text from a migration author and currently interpolate it with no escaping whatsoever.

## Human test plan

- [ ] N/A — mechanical; the proof is a row in `timescaledb_information.hypertables` and a chunk count > 1.
