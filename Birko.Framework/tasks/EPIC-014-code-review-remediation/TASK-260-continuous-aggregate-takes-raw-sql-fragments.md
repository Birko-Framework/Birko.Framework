---
id: TASK-260
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-18
depends-on: [TASK-253, TASK-255]
blocks: []
related: [TASK-253, TASK-255]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# `CreateContinuousAggregate` takes two raw SQL fragments that cannot be contained

Found while grilling **TASK-253**'s plan. Auditing the nine emitters in
`Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs` produced a containment classification, and two
arguments fell outside every containable category.

## What is wrong

`BuildContinuousAggregateSql` interpolates `selectClause` and `groupByClause` as **raw SQL in statement
position** (lines 140 and 134/139/142), not inside a string literal:

```csharp
time_bucket('{timeBucket}', time) AS bucket{groupBySql},
{selectClause}
FROM {sourceTable}
GROUP BY bucket{groupBySql};
```

Every other caller-supplied value in the file can be contained:

| Argument kind | Position | Containment |
|---|---|---|
| table / view / source-table names | regclass inside a literal, or an identifier position | quote + escape, or `QuoteIdentifier` — **complete** ([[TASK-253]]) |
| time / space column | `name` inside a literal | pre-fold + escape — **complete** ([[TASK-253]]) |
| `compress_orderby`, `compress_segmentby`, `timeBucket`, INTERVAL strings | inside a `'…'` literal | `''` doubling — **complete**, you cannot leave a literal |
| **`selectClause`, `groupByClause`** | **raw SQL** | **none possible** |

So these two are not a gap TASK-253 left open — they are inherent to the parameters' type. A `string` that
is documented as "SQL" has no containment story; the only fix is to stop taking SQL.

**TASK-253 documents and pins the boundary** (an XML comment on both parameters, plus a test asserting they
are *not* identifier-validated, so nobody "hardens" them and breaks legitimate aggregate expressions). This
task is the redesign that removes it.

## Why this is a redesign and not a validator

Both were considered and rejected during TASK-253's grill:

- **Identifier-validating `groupByClause`** refuses legitimate expression group-bys — `date_trunc('day', x)`
  is valid SQL in a `GROUP BY` and a real continuous aggregate may want it. It would break working callers
  to contain a caller who controls `selectClause` on the same line anyway.
- **Validating `selectClause`** is not possible in principle: it is a list of aggregate expressions, which
  is arbitrary SQL by definition.

The containment can only come from the *shape* of the API: aggregate function + column + alias as structured
values the builder composes, rather than text the builder concatenates.

## Measure first

- **How many consumers call `CreateContinuousAggregate`?** `Birko.Data.Migrations.TimescaleDB` has exactly
  **one** importer in the family — `Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`
  (verified 2026-08-18). If it calls nothing, this is unused public surface and the redesign is free of
  migration cost; if it does, the old string overload has to survive or the call site moves with it.
- **Is the current method reachable at all?** [[TASK-255]] establishes that `time_bucket`'s column is
  hardcoded to the literal `time`, which no framework-created table has — so it is plausible that
  `CreateContinuousAggregate` **cannot presently succeed on any Birko entity**. If TASK-255 confirms that,
  this redesign has no working behaviour to preserve, which changes the compatibility question entirely.
  That is why this task depends on TASK-255 rather than merely relating to it.

## Acceptance criteria

- [ ] The two measurements above recorded as numbers, and the compatibility decision (keep the string
      overload / replace it) justified from them rather than from caution.
- [ ] A structured surface for the aggregate's projection and grouping, composing identifiers through the
      producers [[TASK-253]] establishes (`RegclassLiteral` / `CatalogueNameLiteral` / `QuoteIdentifier` on
      `AbstractConnectorBase`) rather than inventing a second quoting rule.
- [ ] The aggregate function set is explicit and closed (an enum or equivalent), not a passthrough string —
      a passthrough is the same hole with more ceremony.
- [ ] CR-H071's dangling-comma behaviour and CR-H070's non-hardcoded columns both still hold, with their
      existing tests passing unmodified.
- [ ] An injection test per structured input, showing the payload that reaches the DDL today cannot.
- [ ] Verified against **live TimescaleDB**: a row in `timescaledb_information.continuous_aggregates` and
      rows read back out of the aggregate. Not "the call did not throw" (TASK-209).
- [ ] If the string overload is kept, it is marked obsolete with the reason, and its doc comment still
      carries [[TASK-253]]'s "this is SQL, do not build it from untrusted input" warning.

## Out of scope

- Identifier quoting, folding and escaping in this method — **[[TASK-253]] owns those.**
- The hardcoded `time` bucketing column — **[[TASK-255]] owns that**, and this task is sequenced after it.
- `time_bucket`'s origin / offset / timezone overloads. Adding them is a capability, not containment.
- The other eight emitters in the file. They are all containable and TASK-253 contains them.

## Human test plan

- [ ] N/A — mechanical; the proof is a row in `timescaledb_information.continuous_aggregates` plus rows read
      back from the aggregate, and the injection tests.
