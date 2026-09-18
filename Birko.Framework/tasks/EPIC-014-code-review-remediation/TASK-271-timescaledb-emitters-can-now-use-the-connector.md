---
id: TASK-271
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-243, TASK-253, TASK-259, TASK-260]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB, Birko.Data.SQL]
---

# The TimescaleDB migration emitters bypass the connector for a reason that no longer exists

## Context — a decision reopened by TASK-259, not a new defect

`Birko.Data.Migrations.TimescaleDB`'s emitters (`create_hypertable`, the four policy functions,
`refresh_continuous_aggregate`, the continuous-aggregate DDL) execute on the migration's own connection and
transaction, hand-rolling their own execution rather than going through
`AbstractConnector.DoDdlCommand`.

**TASK-253 chose that deliberately, and its stated reason was this:** routing them through the connector would
have required `AbstractConnector.SetExternalTransaction`, which published one caller's connection onto a
connector cached process-wide per (type, settings id) — the mechanism both stores abandoned in TASK-240, and
which TASK-259 then proved was a live defect in its last caller.

**TASK-259 deleted that mechanism.** `SqlSchemaBuilder` moved onto `AmbientSqlTransaction` — flow-scoped,
keyed by settings id, restores on dispose — and the legacy pair, its setter and its four read branches were
removed. So a migration can now publish its connection and transaction as an ambient boundary safely, and the
constraint that forced the bypass is gone.

This task is to decide whether to take the option that reopened, **not** to assume it should be taken.

## Why it might be worth doing

- The emitters would reuse `DoDdlCommand`, and with it the provider capabilities that funnel already consults
  — `SupportsTransactionalDdl` (TASK-243), `IsMissingTableException`, the retry/serialisation policy.
- They would stop being a second execution path for DDL in a project whose whole history is "the same
  statement written in more than one place drifts" (TASK-245, TASK-246, TASK-247, TASK-253).
- `TimescaleDBConnector : PostgreSQLConnector` already owns the identifier rules these emitters had to
  re-derive (`RegclassLiteral`, `CatalogueNameLiteral`, `FoldsUnquotedIdentifiers`) — TASK-253 converged those
  onto shared producers precisely because the migration project had written them independently and wrongly.

## Why it might not

- It is a **behaviour change on a live TimescaleDB path**. These statements currently run inside the
  migration's transaction unconditionally; through `DoDdlCommand` they become subject to the
  `SupportsTransactionalDdl` capability and the ambient-suppression rule, which is exactly the machinery
  TASK-243 built — and that machinery deliberately takes DDL *off* the boundary on providers whose DDL is not
  transactional. PostgreSQL's DDL **is** transactional, so the intended outcome is "no change", but that is a
  prediction to verify rather than assume.
- TASK-253's other precondition is unaffected and still holds: these rules assume the object was created by
  this framework's DDL (see `TimescaleDBMigration`'s PRECONDITION paragraph and [[TASK-262]]).

## Acceptance criteria

- [ ] Measured against a live **TimescaleDB 2 / PG16** *before* any change: what connection and transaction
      each emitter currently runs on, and whether a failing migration rolls its hypertable conversion back.
- [ ] A decision recorded — route through the connector, or keep the bypass — with its reason. Keeping it is a
      legitimate outcome; if chosen, `TimescaleDBMigration`'s comment must say so as a *decision* rather than
      continuing to cite a mechanism that no longer exists.
- [ ] If routed: the same rollback behaviour demonstrated after the change, and the `SupportsTransactionalDdl`
      interaction asserted rather than reasoned about.
- [ ] Proven able to fail.

## Out of scope

- `BuildContinuousAggregateSql`'s two raw-SQL arguments — [[TASK-260]] owns changing that API's shape.
- Objects not created by this framework's DDL — [[TASK-262]].
- The connector-caching pattern itself — [[TASK-270]].

## Human test plan

- [ ] N/A — mechanical; the proof is which connection the statements run on and whether a rollback undoes the
      hypertable conversion, both observable against a live server.

---

## Merged with TASK-282, 2026-09-08 — and re-prioritised P2 → P3

[[TASK-282]] was filed at TASK-281's close gate as *"an option TASK-259 reopened and nobody owns"* — but
**this task already owned it**, since 2026-08-21. The spawn sweep looked for floating prose and did not
look for an existing task with the same subject, which is worth remembering: *before filing a spawn for
work that has "been floating", search the backlog for it.* TASK-282 is cancelled, pointing here.

Its one piece of better reasoning is adopted, and it lowers the priority:

> Nothing is broken. The current shape works, is tested, and TASK-281 has just measured its most important
> limit (`25001` on two statements) and routed around it **without** needing the connector — which is
> worth noting, because it removes the most obvious argument for doing this. This is a consistency and
> capability-reuse question, not a defect.

So this is P3. It remains a real decision — the emitters reuse none of the connector's provider
capabilities, DDL funnel, retry policy or exception classification — but the case for making it is weaker
than when TASK-259 reopened the option.
