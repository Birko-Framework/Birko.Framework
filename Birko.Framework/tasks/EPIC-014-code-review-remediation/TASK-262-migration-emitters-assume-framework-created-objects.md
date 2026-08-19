---
id: TASK-262
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-209, TASK-253, TASK-472]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# The migration emitters' identifier rules assume this framework created the object — twice over

Both halves came out of **[[TASK-253]]**'s close-gate `code-review`, and both are regressions **that task
introduced**. They are grouped because they are one premise failing in two directions: the quoting and folding
rules were derived from what `AbstractConnector.CreateTable` provably emits, and the *migrations* layer hands
those rules names for objects it did not necessarily create.

Neither is firing: `Birko.Data.Migrations.TimescaleDB` has exactly **one** importer in the family
(`Consumers/Birko.Sandbox`), and TASK-247's sweep of all 16 consumer repos found **0** migration-declared usage.
That measurement is why TASK-253 closed rather than holding — but they are traps laid for the next author, and
the API is public.

## 1. A schema-qualified name is now one identifier containing a dot

`RegclassLiteral("reporting.evts")` emits `'"reporting.evts"'`, and `QuoteIdentifier("reporting.evts")` emits
`"reporting.evts"` — in both cases a *single* identifier whose name literally contains a period.

**Measured on TimescaleDB 2.29.2 / PostgreSQL 16:**

```
SELECT create_hypertable('reporting.evts','ts');      -- (82,reporting,evts,t)   works
SELECT create_hypertable('"reporting.evts2"','ts');   -- ERROR: relation "reporting.evts2" does not exist
```

So `CreateHypertable(ctx, "reporting.evts", "ts")`, `AddCompressionPolicy(ctx, "reporting.evts", …)`,
`AddRetentionPolicy`, `RemoveCompressionPolicy`, `RemoveRetentionPolicy`, `RefreshContinuousAggregate` and
`CreateContinuousAggregate`'s view/source arguments all worked before TASK-253 and now raise `42P01`.

**Why the store path is unaffected**, and why that made this invisible: it takes its table name from
`Table.Name`, which is never schema-qualified. Schema qualification is idiomatic in a *migration* and nowhere
else in the changed surface — so the premise TASK-472 established one layer down does not transfer, and TASK-253
carried it over without noticing.

## 2. The fold and the quote assume framework-emitted DDL, which a raw-SQL migration is not

`CatalogueNameLiteral`'s pre-fold is justified in `TimescaleDBConnector` because `CreateTable` *provably* emits
column definitions bare. In the migrations layer an author can create the object with hand-written SQL — via
`SqlScriptMigration` or any raw `ExecuteScript` — and then the premise is simply false. Two regressions, in
opposite directions:

- **A quoted mixed-case column becomes unaddressable.** `CREATE TABLE metrics ("Timestamp" timestamptz)` then
  `CreateHypertable(ctx, "metrics", "Timestamp")` used to work; the fold now emits `'timestamp'` → `42703`, and
  there is no spelling of the argument that reaches the real column.
- **A bare-created table can no longer be referenced by its source spelling.** `CREATE TABLE Metrics (…)`
  stores `metrics`; `CreateContinuousAggregate(ctx, "Agg", "Metrics", …)` used to work through the parser's own
  folding and now emits `FROM "Metrics"` → `42P01`.

Objects created through `SqlSchemaBuilder` **are** safe — it quotes tables and emits columns bare, so the new
rules are exactly right for them. That is why this is the smaller half.

## Acceptance criteria

- [ ] A decision, recorded with its reasoning, on whether these APIs **support schema qualification** — and if
      they do, qualification handled in every position it can appear (the regclass literal, `ALTER TABLE`,
      `CREATE MATERIALIZED VIEW`, `FROM`), not just the one that prompted the task. Splitting on the last
      unquoted `.` makes a table genuinely named `a.b` unaddressable, so say which trade is being taken.
- [ ] A decision on the raw-SQL premise: either the emitters keep assuming framework-created objects and the
      class remarks state it as a **precondition** (TASK-253 added a first pass at this — check it says what the
      fix ends up meaning), or the fold/quote becomes opt-out for an author who created the object by hand.
- [ ] Verified against **live TimescaleDB** for whichever answers are chosen, including a schema-qualified
      table and a hand-created quoted mixed-case column. Not "the call did not throw" (TASK-209).
- [ ] Proven able to fail: the qualified-name test must go red against today's code — it currently would, which
      is the point of filing it with the measurement attached.
- [ ] The blast radius re-measured rather than inherited from this file: confirm no consumer has started using
      these emitters since 2026-08-18.

## Out of scope

- The identifier rules themselves for framework-created objects — **[[TASK-253]] owns those and they are
  correct**; this task is about the objects that premise does not cover.
- The hardcoded `time` bucketing column (**[[TASK-255]]**), the raw SQL fragments (**[[TASK-260]]**), and
  `GetChunkInterval`'s stale catalogue column (**[[TASK-261]]**).
- `SqlSchemaBuilder`'s uncleared external transaction (**[[TASK-259]]**).

## Human test plan

- [ ] N/A — mechanical; the proof is a hypertable created over a schema-qualified table, and a hand-created
      quoted mixed-case column reachable through the emitter.
