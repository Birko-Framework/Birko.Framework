---
id: TASK-282
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-24
depends-on: []
blocks: []
related: [TASK-259, TASK-253, TASK-281, TASK-243]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# The TimescaleDB migration emitters bypass the connector — an option TASK-259 reopened and nobody owns

Given an id at **[[TASK-281]]**'s close-gate out-of-scope sweep. It has been floating as prose since
TASK-259 (2026-08-21), referenced from a class remark, from TASK-253's rationale and from TASK-281's
`## Out of scope`, and owned by nothing — which is the shape § Conventions calls a skipped spawn.

## What is open

`TimescaleDBMigration`'s nine emitters execute through their own `ExecuteScript` on the migration's
connection and transaction. They do **not** go through `AbstractConnector.DoDdlCommand`, so they reuse
none of the connector's machinery: the provider capabilities (`SupportsTransactionalDdl`,
`FoldsUnquotedIdentifiers`), the DDL funnel, the retry policy, the exception classification.

**TASK-253 rejected routing them through the connector for a reason that no longer exists.** Doing so
required `SetExternalTransaction`, which published one caller's connection onto a connector cached
process-wide per (type, settings id) — the mechanism both stores abandoned in TASK-240, and a live defect
in its last caller. **TASK-259 deleted that mechanism entirely**: `SqlSchemaBuilder` moved onto
`AmbientSqlTransaction`, which is flow-scoped and restores on dispose. So a migration can now publish its
connection and transaction as an ambient boundary safely, and the constraint that forced the current shape
is gone.

TASK-259 recorded this on the class rather than acting on it, deliberately — it is a behaviour change on a
live TimescaleDB path and wants its own measurement, not a comment edit. That reasoning still holds; what
was missing is an id.

## Why it is P3 rather than P2

Nothing is broken. The current shape works, is tested, and TASK-281 has just measured its most important
limit (`25001` on two statements) and routed around it **without** needing the connector — which is worth
noting, because it removes the most obvious argument for doing this. This is a consistency and
capability-reuse question, not a defect.

## Acceptance criteria

- [ ] Decide, from a measurement rather than from taste, whether routing through the connector is worth the
      behaviour change. State what it would actually buy — name the specific capabilities and the concrete
      case each one improves, not "consistency".
- [ ] **Check the `25001` interaction first, because it is the trap.** TASK-281 established that
      `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` and `refresh_continuous_aggregate` cannot
      run in a transaction block. `DoDdlCommand` consults `SupportsTransactionalDdl` and can *suppress* an
      ambient boundary (TASK-243) — so routing might interact with TASK-281's `WITH NO DATA` and refusal in
      ways that are either an improvement or a silent regression. Measure both, on a live server.
- [ ] If routed, the identifier treatments must be **preserved exactly**, not re-derived: the four
      positions TASK-253/262 established (`RegclassLiteral`, `CatalogueNameLiteral`, `QualifiedIdentifier`,
      `EscapeLiteral`-only) are load-bearing and each has a live test. A rewrite that reasons them out again
      is how this family has repeatedly regressed.
- [ ] If **not** routed, say so on the class in place of the current "the choice is now open" remark, with
      the reason — so the next reader meets a decision rather than a third invitation to reconsider.
- [ ] Either outcome closes this task. **A decision not to do it is a valid result** and must be recorded as
      one, not left as another open paragraph.

## Out of scope

- The `25001` handling itself — **[[TASK-281]]** owns it and has landed.
- The schema-qualified catalogue lookups — **[[TASK-280]]** owns those.
- `AmbientSqlTransaction`'s own semantics; this task consumes it, it does not change it.

## Human test plan

- [ ] N/A — mechanical, and possibly a documentation-only outcome; the proof is either a live-verified
      behaviour-preserving rewrite or a recorded decision with its reason.
