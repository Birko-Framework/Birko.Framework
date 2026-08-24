---
id: TASK-280
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-24
depends-on: []
blocks: []
related: [TASK-262, TASK-261, TASK-255]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# `IsHypertable` and `GetChunkInterval` ignore the schema half of the qualified name TASK-262 taught them to accept

Found by `code-review` at **[[TASK-255]]**'s close gate. Out of scope there — TASK-255 changed only
`BuildContinuousAggregateSql` — and pre-existing rather than introduced: the reviewer attributed it to that
task's diff, but the code is committed in `9b7f943` (TASK-262) and `b9566d9` (TASK-261).

## What is wrong

TASK-262 made every object-name argument accept a schema-qualified name through `QualifiedIdentifier`, and
the class remarks now advertise that. The two **catalogue-reading** methods did not follow: they bind the
caller's `tableName` verbatim into

```sql
WHERE hypertable_name = @table
```

and `timescaledb_information.hypertables.hypertable_name` holds the **bare** table name, with the schema in a
separate `hypertable_schema` column. Two consequences:

- **A qualified name matches nothing.** `CreateHypertable(ctx, "reporting.Evts", "Ts")` succeeds — it emits
  `'"reporting"."Evts"'` — and `IsHypertable(ctx, "reporting.Evts")` then answers **false**, while
  `GetChunkInterval(ctx, "reporting.Evts")` returns **null**, which that method's own doc defines as *"not a
  hypertable"*. A wrong answer wearing the costume of a legitimate absence.
- **An unqualified name is ambiguous.** With no `hypertable_schema` filter and no `ORDER BY`, if both
  `public.Evts` and `reporting.Evts` are hypertables then `GetChunkInterval(ctx, "Evts")` returns whichever
  row the planner emits first — arbitrarily the wrong schema's interval. Same shape as TASK-261's
  `dimension_number` finding, one level up: *a view with one row per object needs its row pinned, and
  `ExecuteScalar` will not tell you.*

## Blast radius — measure first, do not inherit this estimate

Unmeasured. TASK-255 measured **0 of 16** consumer repos calling any emitter in this class and **1**
(`Birko.Sandbox`) merely importing the `.projitems`, so this is very likely latent — but that count was taken
on 2026-08-24 for a different method and § Conventions (TASK-259) is explicit that a stale blast radius is how
a claim gets written into a commit message and then corrected.

## Acceptance criteria

- [ ] Both methods resolve the schema half: a qualified `tableName` is split and matched against
      `hypertable_schema` **and** `hypertable_name`. Reuse the split that
      `AbstractConnectorBase.QualifiedIdentifier` already performs rather than writing a second one — the
      one-producer rule, and the shape TASK-262 established (its splitter is unquoted-dot-aware, so
      `"a.b"` stays one part).
- [ ] An **unqualified** name keeps working exactly as it does today for the single-schema case, which is
      every current caller. State whether it should then match any schema or default to the connection's
      search path, and answer it from a measurement on a live server rather than from taste.
- [ ] The ambiguity is closed rather than reordered: two same-named hypertables in different schemas must
      give a deterministic, correct answer, not merely a stable one.
- [ ] Verified against **live TimescaleDB** with two hypertables of the same name in different schemas —
      the fixture that distinguishes a fix from a no-op. Asserting against a single-schema database cannot.
- [ ] Proven able to fail: revert the schema filter and watch the two-schema test go red while the existing
      single-schema tests stay green (they are the control, and
      `QualifiedNameEmitterLiveTests.The_hypertable_probe_answers_for_a_qualified_table` already pins the
      `IsHypertable` half).
- [ ] The class-level remark claiming *"every object-name argument"* is qualified-safe is corrected or
      narrowed in the same change — it is currently false for these two methods, which is what let this sit
      unnoticed.

## Out of scope

- The emitters that build DDL (`create_hypertable`, the policy functions, the aggregate) — **[[TASK-262]]
  fixed those** and they are not affected.
- The column-name fold limit (a hand-created quoted mixed-case column is unreachable) — recorded on the
  class by TASK-262 as a deliberate limit, with the reason it cannot be made conditional.

## Human test plan

- [ ] N/A — mechanical; the proof is a live two-schema fixture returning the right interval for each, which
      no human inspection improves on.
