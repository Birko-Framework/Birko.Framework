---
id: TASK-279
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-24
depends-on: []
blocks: []
related: [TASK-255, TASK-253]
findings: [CR-H070]
pr: "Birko.Data.Migrations.TimescaleDB 47a0d4c + .Tests c13c170"
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# `BuildCompressionPolicySql` keeps CR-H070's `orderByColumn = "time"` — the half of the remedy that was a compatibility artefact

Found while grilling **[[TASK-255]]**'s implementation plan, which fixes the *same finding* in the
neighbouring `BuildContinuousAggregateSql`. TASK-255's criteria name only that method, so this is a spawn
rather than a widening.

## What is wrong

`TimescaleDBMigration.AddCompressionPolicy` / `BuildCompressionPolicySql` (lines ~180 and ~204) declare:

```csharp
string orderByColumn = "time"
```

By the same reasoning TASK-255 applies to its neighbour, **no framework-created table can have a column
named `time`**: column definitions are emitted bare, every Birko entity is PascalCase, so a declared
`Timestamp` is stored `timestamp` and `Ts` is stored `ts`. So the default is unreachable for every Birko
entity — *a default that cannot work on any Birko entity is a silent no-op wearing a parameter's name*
(§ Conventions, TASK-245).

## Why the default exists — measured, not guessed

`git show 531d816` ("fix(migrations): correct TimescaleDB DDL generation (CR-H069/H070/H071)") states the
remedy verbatim:

> *"AddCompressionPolicy no longer hardcodes compress_orderby='time' / compress_segmentby='device_id';
> adds optional orderByColumn (default 'time') and segmentByColumn (default null, line omitted when
> unset) (CR-H070)."*

and the diff shows the method had **no such parameter beforehand**:

```diff
-  protected virtual void AddCompressionPolicy(IMigrationContext context, string tableName, string compressAfterInterval)
+  protected virtual void AddCompressionPolicy(..., string orderByColumn = "time", string? segmentByColumn = null)
```

So the default was a **source-compatibility artefact** — it kept then-existing calls compiling — **not a
judgement that `"time"` is a good value.** That is the distinction this task turns on, and it is why the
fix is not simply "TASK-255 did it, do the same".

## Blast radius — measured at TASK-255's grill

- **The default is never exercised.** The only call sites are the live suite's probe wrapper,
  `MigrationEmitterLiveTests.cs:133` — `Compression(c, t, after, orderBy) => AddCompressionPolicy(c, t, after, orderBy)`
  — which passes `orderBy` explicitly. No other caller anywhere.
- **0 of 16** consumer repos call `AddCompressionPolicy` / `BuildCompressionPolicySql`; **1 of 16**
  (`Birko.Sandbox`) merely imports the `.projitems`, so the surface is compiled but not invoked.
- So the compatibility the default was added to preserve **no longer has anything to preserve**.

## Acceptance criteria

- [x] Re-measure the two counts above before changing anything — they were taken on 2026-08-24 at
      TASK-255's grill and this task may be picked much later. A stale blast radius is what got TASK-247's
      "0 uses of `ISchemaBuilder`" claim corrected after the fact (§ Conventions, TASK-259).
- [x] `orderByColumn` becomes **required** on both `AddCompressionPolicy` and `BuildCompressionPolicySql`,
      *if and only if* the re-measurement still shows no caller relying on the default. If a caller has
      appeared, say so and keep the default — the decision is the measurement's, not symmetry with
      TASK-255.
- [x] `segmentByColumn` is explicitly **left alone** and the reason recorded: it is `null`-defaulted and
      genuinely optional (the `compress_segmentby` line is omitted when unset), which is a different thing
      from a default that cannot work.
- [x] The `compress_orderby` value keeps its **expression-fragment** treatment — `ts DESC` is legitimate,
      so it is escaped for its literal and **not** identifier-validated. Do not "unify" it with
      TASK-255's column guard; that would refuse working migrations. A test pins the direction keyword
      (`CompressionPolicy_AcceptsADirectionKeyword`) and must stay green.
- [x] The doc comment recording CR-H070 is updated so the finding reads as fully closed across both
      methods, rather than half-closed with the other half unexplained.
- [x] Proven able to fail: a mutation restoring the default reds at least one test — a reflection pin on
      `HasDefaultValue`, per § Conventions (TASK-117), since required-ness is otherwise invisible to the
      suite. TASK-255 establishes that shape.

## Out of scope

- `BuildContinuousAggregateSql`'s hardcoded bucketing column — **[[TASK-255]] owns it**, and lands first.
  This task should be picked *after* it, so the doc-comment edits do not conflict.
- Replacing `selectClause` / `groupByClause` with a structured surface — **[[TASK-260]] owns that**.
- Adding identifier validation to `compress_orderby` / `compress_segmentby`. They are expression
  fragments inside a quoted literal, where escaping is complete containment; validating them would refuse
  legitimate values. See the fourth criterion.

## Human test plan

- [x] N/A — mechanical; the proof is a reflection assertion that the parameter carries no default, plus the
      existing live compression tests staying green.

---

## Worked 2026-09-08

### ⚠ Criterion 1 caught a stale count — the task's own blast radius was wrong

This file said the default *"is never exercised"* and *"the only call sites are the live suite's probe
wrapper … No other caller anywhere."* Re-measured 2026-09-08:

| claim | re-measured |
|---|---|
| 0 of 16 consumer repos call it | **0 of 16** — unchanged, and this is the number that decides |
| "no other caller anywhere" relies on the default | **4 test call sites do** — `TimescaleDBMigrationSqlTests` ×2, `TimescaleDBMigrationInjectionTests` ×2 |

The criterion said to keep the default if a caller had appeared. The callers that appeared are the
framework's **own tests**, updated in the same commit — not someone the change can break — so the fix
proceeded, with the count corrected rather than quietly reused. Fourth stale count re-measured this week.

### The silent-rebind hazard TASK-255 recorded does NOT apply here, and that is worth stating

TASK-255 warns that with all-`string` parameters a 6-argument call can **silently rebind** when a
parameter is inserted, and that it was affordable only at 0 consumers. Here nothing is inserted — a
parameter merely loses its default — so arity and order are unchanged and no call can rebind. Measured:
all 4 affected sites failed with **`CS7036`**, loudly, which is the whole difference.

### ⚠ A test was pinning the default, again

`CompressionPolicy_DefaultsOrderByTime_AndOmitsSegmentBy` asserted
`timescaledb.compress_orderby = 'time'` — so the default was a *named, asserted contract*, which is why it
survived CR-H070's own remediation. Exactly TASK-284's shape one day earlier, where an `[InlineData("")]`
row pinned that defect. Renamed to `CompressionPolicy_OmitsSegmentBy_WhenItIsNotSupplied`, keeping the
half that is CR-H070's real point, with the history in its remarks.

### `segmentByColumn` deliberately untouched

Its `null` default is a *working* "no segmenting" — the `compress_segmentby` line is omitted when unset —
which is a different thing from a value that cannot apply. The reflection pin asserts **both** sides, so a
change that stripped both defaults cannot pass half the test unnoticed.

### Verified

`BIRKO_REQUIRE_LIVE` set against live TimescaleDB 2.29.2 / PostgreSQL 16: Migrations.TimescaleDB **85
passed** (84 → 85), 0 failed. **Mutation:** restoring the default reds exactly the reflection pin and
nothing else — required-ness is invisible to every other test in the file, which is why that pin exists.
`CompressionPolicy_AcceptsADirectionKeyword` stayed green throughout, so the expression-fragment treatment
is intact and `ts DESC` is still legitimate.
