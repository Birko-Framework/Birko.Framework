---
id: TASK-260
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-18
depends-on: [TASK-253, TASK-255]
blocks: []
related: [TASK-253, TASK-255]
findings: []
pr: "Birko.Data.Migrations.TimescaleDB e6257af · Birko.Data.Migrations.TimescaleDB.Tests 608fd84"
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

- [x] The two measurements above recorded as numbers, and the compatibility decision (keep the string
      overload / replace it) justified from them rather than from caution.
- [x] A structured surface for the aggregate's projection and grouping, composing identifiers through the
      producers [[TASK-253]] establishes (`RegclassLiteral` / `CatalogueNameLiteral` / `QuoteIdentifier` on
      `AbstractConnectorBase`) rather than inventing a second quoting rule.
- [x] The aggregate function set is explicit and closed (an enum or equivalent), not a passthrough string —
      a passthrough is the same hole with more ceremony.
      **⚠ AMENDED 2026-08-26, on measurement, at the plan grill.** The *purpose* — containment — stands and is
      met. The *letter* does not: TimescaleDB 2.29.2 accepts essentially any aggregate in a continuous
      aggregate, including `array_agg`, `string_agg`, `bool_and` and the ordered-set `percentile_cont`, so a
      closed enum imposes a **new** restriction rather than mirroring the server's, and would refuse
      aggregates that work today. The function is instead a **validated bare identifier** — not a passthrough:
      arbitrary text fails the guard, and what survives can only fail the statement, loudly, at DDL time
      (`42883`, naming the function and its argument types). Amended openly rather than quietly satisfied.
- [x] CR-H071's dangling-comma behaviour and CR-H070's non-hardcoded columns both still hold, with their
      existing tests passing unmodified.
- [x] An injection test per structured input, showing the payload that reaches the DDL today cannot.
- [x] Verified against **live TimescaleDB**: a row in `timescaledb_information.continuous_aggregates` and
      rows read back out of the aggregate. Not "the call did not throw" (TASK-209).
- [x] If the string overload is kept, it is marked obsolete with the reason, and its doc comment still
      carries [[TASK-253]]'s "this is SQL, do not build it from untrusted input" warning.

## Out of scope

- Identifier quoting, folding and escaping in this method — **[[TASK-253]] owns those.**
- The hardcoded `time` bucketing column — **[[TASK-255]] owns that**, and this task is sequenced after it.
- `time_bucket`'s origin / offset / timezone overloads. Adding them is a capability, not containment.
- The other eight emitters in the file. They are all containable and TASK-253 contains them.
- `AddContinuousAggregatePolicy`'s empty-vs-null `startOffset`, and the untested null-startOffset escape hatch
  its sibling refusal names — **[[TASK-284]] owns both**, spawned at this task's close gate. They are
  TASK-281's policy emitter, not this task's projection/grouping surface.
- `IsHypertable` / `GetChunkInterval` ignoring the schema half of a qualified name — **[[TASK-280]] owns it**;
  the class remark that wrongly denied the limitation is corrected here.

## Implementation plan

_Drafted 2026-08-25, then grilled against a live server. The grill overturned two of the plan's positions and
amended an acceptance criterion — both recorded below rather than quietly absorbed._

### Step 0 — measurements. All done, on live TimescaleDB 2.29.2 / PostgreSQL 16.15.

- **M1 · Consumers: ZERO.** 0 of 16 consumer repos call `CreateContinuousAggregate`; 1 (`Birko.Sandbox`)
  imports the `.projitems` without invoking it. **No external call site moves.**
- **M2 · Reachability: the task's premise INVERTED during this run.** It predicted TASK-255 might show the
  method could not succeed on any Birko entity. TASK-255 confirmed that **and fixed it**; TASK-281 then found
  it still failed through the runner (`25001`) **and fixed that**. So the method acquired working behaviour
  from the two tasks this one depends on.
- **M3 · The only callers are in-tree tests — and the first count of them was WRONG.** Recorded as "three"
  during planning; the build proved **14 call sites across 4 files**
  (`TimescaleDBMigrationSqlTests`, `TimescaleDBMigrationInjectionTests`, `MigrationEmitterLiveTests`,
  `ContinuousAggregateRunnerPathTests`). The error: the grep covered only `CreateContinuousAggregate(` — the
  `protected virtual` wrapper — and missed `BuildContinuousAggregateSql(`, the `internal static` the tests
  mostly call. **Corrected at the close gate by `verify-intent`, which judged the criterion against the diff
  rather than against its tick.** The compatibility decision is unaffected (0 external callers either way),
  but a measurement recorded as a number has to be the right number.
  <br>**Worth carrying:** the removal was nevertheless *loud at every one of the 14 sites* — `CS1503`, because
  the parameter **type** changed. Contrast TASK-255, where inserting a same-typed `string` parameter let two
  6-argument calls silently rebind. Changing an arity is dangerous; changing a type is not.
- **M4 · Which aggregates a continuous aggregate accepts — and it is NOT a restricted set.** `count(*)`,
  `count(x)`, `sum`, `avg`, `min`, `max`, `stddev`, `variance`, `first(v, t)`, **`array_agg`**,
  **`string_agg`**, **`bool_and`** and even the ordered-set **`percentile_cont(0.5) WITHIN GROUP (...)`** all
  create successfully. **This falsifies the premise a closed enum would have rested on.**
- **M5 · `First`/`Last` are strictly two-argument.** `first(Value, Ts)` works; `first(Value)` → *function
  first(double precision) does not exist*. Measured rather than guessed.
- **M6 · An expression GROUP BY is legal in a continuous aggregate.** `GROUP BY bucket, date_trunc('day', Ts)`
  creates successfully alongside the time bucket.
- **M7 · Every malformed function self-reports at DDL time.** Unknown name → `42883 function
  nosuchagg(double precision) does not exist` + HINT; a non-aggregate (`abs`) → *column must appear in the
  GROUP BY clause*; wrong argument type or arity → `42883` naming the types. **Nothing swallows it**:
  `42883` is not `42P01` so `IsMissingTableException` does not classify it, `ExecuteScript` has no
  `try`/`catch`, and TASK-254's degrade is on the store path, not this migration path.
- **M8 · A null column rendering as `*` is safe.** `count(*)` works; `sum(*)` / `avg(*)` → *function sum()
  does not exist*.

### Step 1 — the surface: a validated identifier, NOT a closed enum

**⚠ This amends acceptance criterion 3, deliberately and on evidence.** The criterion requires the function
set to be *"explicit and closed (an enum or equivalent), not a passthrough string — a passthrough is the same
hole with more ceremony"*. Its **purpose** is containment; its **letter** assumes the legal set is small. M4
falsifies that: TimescaleDB accepts essentially any aggregate, including user-defined ones. A closed enum
would therefore impose a brand-new restriction and refuse aggregates that work today — the wrong kind of
guard.

So the function is a **bare identifier, validated** by
`Birko.Data.SQL.DataBase.ValidateColumnIdentifier` — the same producer TASK-255 introduced, so this file has
**one rule for "a name interpolated bare into a statement"** rather than two.

**A passthrough this is not, and M7 is the evidence.** A passthrough accepts arbitrary *text*; this accepts a
single bare identifier and nothing else — `avg(x); DROP TABLE t; --` fails the guard. What survives can only
*fail* the statement, never injure it, and it fails loudly at DDL time with a better message than a
hand-written whitelist would produce. That is exactly the property `ValidateColumnIdentifier` already claims:
*a bare identifier that names no column is at worst a database error, which is a wrong answer that reports
itself.*

### Step 2 — the types

```csharp
public sealed class ContinuousAggregateProjection   // function + optional column + alias
public sealed class ContinuousAggregateGrouping     // function? + optional literal arg + column
```

- **Column and alias** → `ValidateColumnIdentifier`. **Function name** → the same guard.
- **A null column renders `*`** (M8). The `*` is emitted by the framework, never caller text, so it is
  contained by construction; a function for which it is meaningless self-reports.
- **Two-argument aggregates** (`first`/`last`, M5) need a second column, so the projection carries an
  optional second column rather than pretending every aggregate is unary.
- **Grouping is the SAME structured shape** (decision G2). A bare column is a grouping with no function; a
  `date_trunc('day', Ts)` is function + literal + column, with the literal contained by
  `SqlLiteral.EscapeLiteral` — already used in this file for the time bucket.

**Why grouping is not columns-only:** M6 measured expression grouping as legal, and with an open projection,
refusing it would be arbitrary rather than principled. **The plan's original claim that expression grouping
is "unusual" is withdrawn** — plausible, unmeasured, and contradicted.

### Step 3 — compatibility: replace outright

No `[Obsolete]` string overload. **The justification is the task's own definition of done**, not TASK-247's
dead-fallback rule — an earlier draft cited TASK-247, which is about *drift* and is the weaker argument. The
task states *"the only fix is to stop taking SQL"*; an obsolete overload leaves the uncontainable surface
callable and the defect reachable, so keeping it fails the task rather than softening it. Criterion 7 is
conditional (*"If the string overload is kept…"*), not a requirement to keep one.

Affordable because of M1 (0 external callers) and M3 (3 in-tree test call sites, which move with the change).

### Step 4 — what must not change

CR-H070's `timeColumn` parameter (TASK-255), CR-H071's dangling-comma behaviour, TASK-281's `WITH NO DATA`,
the `RefreshContinuousAggregate` refusal, and `AddContinuousAggregatePolicy`. Their tests pass as they now
stand.

### Step 5 — tests

- **Invert first, watch it fail.** `ContinuousAggregate_LeavesExpressionClausesIntact` pins the boundary this
  task removes; it becomes the structured equivalent, asserting `date_trunc('day', Ts)` **still reachable**
  through G2 — **not** a capability loss, which is what the first draft wrongly assumed.
- **Injection test per structured input**: function, column, alias each refuse the payload that reaches the
  DDL today. Containment here is **by refusal**, TASK-255's third mechanism.
- **Live** (criterion 6): the catalogue row **and rows read back out of the aggregate** (TASK-209 —
  a non-throwing DDL call proves nothing here). TASK-281's `bucketsRowsItCanBeReadBackFrom` is the model.
- **A grouping-free aggregate** still emits `GROUP BY bucket` with no dangling comma (CR-H071).
- **`count(*)`** via a null column.
- **Mutations**, each red ≥ 1: drop the function validation; drop the column validation; drop the alias
  validation; emit a dangling comma for zero groupings; drop `EscapeLiteral` from the grouping literal.

### Step 6 — commits (polyrepo, production before aggregator, no `Co-Authored-By`)

Three: `Birko.Data.Migrations.TimescaleDB`, `…TimescaleDB.Tests` (carrying the three migrated call sites),
aggregator.

### Deferred

- **Ordered-set aggregates** (`percentile_cont(0.5) WITHIN GROUP (ORDER BY x)`) — legal in a continuous
  aggregate (M4) but their syntax is not expressible as function + args. Unblock condition: a caller needing
  one; it wants its own structured shape, never a raw string.

### Results — measured 2026-08-25/26

Live **TimescaleDB 2.29.2 / PostgreSQL 16.15**, `BIRKO_REQUIRE_LIVE=1`:
`Birko.Data.Migrations.TimescaleDB.Tests` **81 passed, 0 failed, 0 skipped** (72 → 81, **+9**), no new
nullable warning.

**Mutations — six, every one red ≥ 1:**

| # | Mutation | Measured |
|---|---|---|
| M1 | drop the **function** validation | 1 |
| M2 | drop the **alias** validation | 1 |
| M3 | drop the grouping **literal escaping** | 1 |
| M4 | emit a dangling comma for zero groupings | **10** — CR-H071's guard reaches the live tests too |
| M5 | SELECT list without the grouping alias | 2 |
| M6 | accept a null column again (the `*` sentinel) | 1 |

### Close gate — four verdicts, and it changed the shipped result

**Standards:** two blockers, both fixed — a class remark that asserted the old state, and
register-on-introduce (§ Conventions entry added after TASK-255's, which this completes).

**Fidelity:** one real finding — **the recorded call-site count was wrong** (3 vs 14, because the grep
covered only the `protected virtual` wrapper and missed the `internal static` the tests call). Corrected in
M3 above. Two further observations recorded rather than dressed as passes: criterion 2 names producers that
predate the right one by six days, and criterion 4's *"tests passing unmodified"* is unsatisfiable under any
API change — the **assertions** are unmodified, only the invocations changed.

**Security:** this change *is* the security fix. The attack surface is removed rather than relocated (no
`{selectClause}` / `{groupByClause}` remains anywhere), every new input is contained, and three of the
guards are mutation-proven independently.

**Correctness:** six findings. Three were fixed in scope and **one of them corrected a claim made to the
user mid-task**:

- Stale method `<remarks>` still documenting the removed parameters — and they would be `CS1734` under
  `GenerateDocumentationFile`. The class-level doc had been corrected one screen above; the method's own had
  not.
- **`null` silently became the `*` sentinel.** `Of("count", null, "n")` rendered `count(*)` — counting every
  row instead of the intended column's non-null values — because the sentinel branch ran *before*
  validation. A wrong answer that does not report itself, unlike `""`, which the guard refuses. Now rejected,
  with the message naming `OfAll` as the legitimate door.
- **⚠ A capability regression asserted not to exist.** The plan, the grill and a message to the user all said
  the expression group-by was preserved. Measured at the gate: **two same-function groupings collide** with
  `42701 column "date_trunc" specified more than once`, because groupings are spliced into the SELECT list
  unaliased and the raw string being replaced *could* write `date_trunc('day', Ts) AS day`. The grouping
  survived; **aliasing it did not.** Groupings now carry an optional alias, and the SELECT and GROUP BY forms
  deliberately differ — an alias in `GROUP BY` is a syntax error — so CR-H071's guard is re-expressed on the
  **emptiness of the grouping set** rather than on the two strings being identical.

One corrected in place: the class remark claimed *"Every object-name argument goes through
`QualifiedIdentifier`"*, which is false for `IsHypertable` and `GetChunkInterval` (they compare against the
catalogue's bare name). [[TASK-280]] owns the fix; the sentence now says so.

Two spawned as **[[TASK-284]]**, both against TASK-281's policy emitter rather than this task: `""` and
`null` are treated identically for `startOffset`, so an empty configuration value silently widens a policy to
all of history while every sibling interval fails loudly on `""`; and the *"pass a null startOffset"* escape
hatch the refusal names has never actually been sent to the server — § SH-H037's own rule, unapplied to the
second door in its own message.

## Human test plan

- [x] N/A — mechanical; the proof is a row in `timescaledb_information.continuous_aggregates` plus rows read
      back from the aggregate, and the injection tests.
