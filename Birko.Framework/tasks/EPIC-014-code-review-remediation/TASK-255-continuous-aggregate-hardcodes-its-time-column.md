---
id: TASK-255
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-253, TASK-472]
findings: [CR-H070]
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# `BuildContinuousAggregateSql` still hardcodes `time` — CR-H070 unfixed in the method next door

Found while planning **TASK-253** (identifier quoting and folding across the same file), which audited
all nine emitters in `Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs` and turned this up as a
**different defect class** in one of them.

## What is wrong

`TimescaleDBMigration.BuildContinuousAggregateSql` (line 139) emits:

```csharp
time_bucket('{timeBucket}', time) AS bucket{groupBySql},
```

The bucketing column is the literal `time`. It is not a parameter of the method, so a caller cannot
supply it, and there is no overload that can.

**This is CR-H070, in the method immediately below the one CR-H070 was filed against.** That finding is
recorded verbatim in this same file, on `BuildCompressionPolicySql`'s doc comment:

> *"Don't hardcode the order/segment columns (CR-H070: 'time'/'device_id' fail on any table without a
> literal device_id column and are wrong for most schemas)."*

The compression-policy emitter was fixed for it — `orderByColumn` / `segmentByColumn` became parameters
— and the continuous-aggregate emitter beside it was not. So the reasoning was written down, applied to
one method, and the neighbour kept the defect with the explanation sitting four lines above it.

## Blast radius — measure before fixing

Unmeasured, deliberately: TASK-253 was a planning pass, not a run. What is known statically:

- Birko entities are PascalCase by convention and their columns are emitted **bare**, so PostgreSQL
  stores them folded — a time column declared `Timestamp` is stored `timestamp`, `Ts` is stored `ts`.
  Neither is `time`. So a continuous aggregate over a framework-created table should fail on
  `42703 column "time" does not exist` for every entity that does not happen to have a column literally
  named `time`.
- `time` is **not** a reserved word in PostgreSQL as a column name, so it is a legal name and a
  hand-made table could carry it. That is the only shape this method currently works for.
- Unlike TASK-253's defects, this one is expected to be **loud**: `ExecuteScript` has no `try`/`catch`
  and `TimescaleDBMigrationRunner.ExecuteSingleMigration` adds none, so the migration should fail rather
  than silently skip. Confirm that — TASK-253's step 5 measures the same question for the sibling
  emitters and the answer should be reused, not re-derived.

Whether any consumer calls this at all is **unknown and worth checking first**: TASK-247's sweep of all
16 consumer repos found 0 uses of `ISchemaBuilder` and 0 migration-declared indexes, so the honest
possibility is that this is latent public surface rather than a firing defect. Say which it is — TASK-246
had to be corrected after the fact for claiming live impact it did not have.

## Acceptance criteria

- [ ] `BuildContinuousAggregateSql` takes the time column as a parameter, following
      `BuildCompressionPolicySql`'s shape exactly (CR-H070's own remedy applied to its neighbour) rather
      than a new convention.
- [ ] The parameter's default is chosen **from a measurement, not from symmetry**: `"time"` preserves
      today's behaviour for any caller that works, but no framework-created table can have that column,
      so state whether the default should be required instead. A default that cannot work on any Birko
      entity is a silent no-op wearing a parameter's name (§ Conventions, TASK-245).
- [ ] The `GROUP BY` / SELECT-list interaction is re-checked with the new parameter — CR-H071's
      dangling-comma guard must still hold, and its two existing tests must still pass.
- [ ] Verified against **live TimescaleDB** by asserting a row in
      `timescaledb_information.continuous_aggregates` for a PascalCase table whose time column is *not*
      named `time`, and by reading rows back out of the aggregate. Not "the call did not throw"
      (TASK-209).
- [ ] Proven able to fail: restore the hardcoded `time` and watch the new live test go red while the
      existing `metrics`/`time` offline tests stay green — they are the discrimination control.
- [ ] Blast radius recorded as a number: how many consumer repos call `CreateContinuousAggregate`, and
      whether this defect is firing or latent.

## Out of scope

- Identifier quoting, folding and literal escaping in this same method (`viewName`, `sourceTable`,
  `timeBucket`, `selectClause`, `groupByClause`) — **[[TASK-253]] owns those**, and it will have touched
  this method already. Sequence after it to avoid a conflicting edit, or rebase onto it.
- `time_bucket`'s other arguments (origin, offset, timezone overloads). Adding them is a capability, not
  a defect fix.
- The `selectClause` / `groupByClause` arguments remaining caller-trusted SQL fragments. TASK-253 pins
  that boundary deliberately; widening it is a separate decision.

## Human test plan

- [ ] N/A — mechanical; the proof is a row in `timescaledb_information.continuous_aggregates` over a
      table whose time column is not named `time`, plus rows read back from the aggregate.
