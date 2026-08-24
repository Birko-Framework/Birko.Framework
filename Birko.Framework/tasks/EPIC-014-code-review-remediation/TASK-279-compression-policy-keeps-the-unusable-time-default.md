---
id: TASK-279
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-24
depends-on: []
blocks: []
related: [TASK-255, TASK-253]
findings: [CR-H070]
pr: null
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

- [ ] Re-measure the two counts above before changing anything — they were taken on 2026-08-24 at
      TASK-255's grill and this task may be picked much later. A stale blast radius is what got TASK-247's
      "0 uses of `ISchemaBuilder`" claim corrected after the fact (§ Conventions, TASK-259).
- [ ] `orderByColumn` becomes **required** on both `AddCompressionPolicy` and `BuildCompressionPolicySql`,
      *if and only if* the re-measurement still shows no caller relying on the default. If a caller has
      appeared, say so and keep the default — the decision is the measurement's, not symmetry with
      TASK-255.
- [ ] `segmentByColumn` is explicitly **left alone** and the reason recorded: it is `null`-defaulted and
      genuinely optional (the `compress_segmentby` line is omitted when unset), which is a different thing
      from a default that cannot work.
- [ ] The `compress_orderby` value keeps its **expression-fragment** treatment — `ts DESC` is legitimate,
      so it is escaped for its literal and **not** identifier-validated. Do not "unify" it with
      TASK-255's column guard; that would refuse working migrations. A test pins the direction keyword
      (`CompressionPolicy_AcceptsADirectionKeyword`) and must stay green.
- [ ] The doc comment recording CR-H070 is updated so the finding reads as fully closed across both
      methods, rather than half-closed with the other half unexplained.
- [ ] Proven able to fail: a mutation restoring the default reds at least one test — a reflection pin on
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

- [ ] N/A — mechanical; the proof is a reflection assertion that the parameter carries no default, plus the
      existing live compression tests staying green.
