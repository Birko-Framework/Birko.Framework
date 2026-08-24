---
id: TASK-281
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-24
depends-on: []
blocks: []
related: [TASK-255, TASK-259, TASK-243]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# A continuous aggregate cannot be created or refreshed inside a transaction — so it may never have worked through the runner at all

Found by `code-review` at **[[TASK-255]]**'s close gate, and rated the most valuable thing that review
produced. Out of scope there (TASK-255 changed the bucketing column, not the execution model) and
pre-existing — both emitters predate it.

## What is wrong — a hypothesis with a mechanism, to be measured before it is believed

PostgreSQL/TimescaleDB refuse two of this class's statements inside a transaction block:

- `refresh_continuous_aggregate` cannot run in a transaction block.
- `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` **without `WITH NO DATA`** performs an initial
  refresh, which carries the same restriction.

`ExecuteScript` sets `command.Transaction = transaction`, and `SqlMigrationSettings.UseTransaction`
**defaults to true**. If the restriction holds as described, then a migration calling
`CreateContinuousAggregate` or `RefreshContinuousAggregate` through `TimescaleDBMigrationRunner` in its
**default configuration fails** — i.e. the feature has never worked on the only path a real migration takes.

**Why nothing caught it.** Every test of these two emitters — including the live ones TASK-255 added — calls
`Exec(BuildContinuousAggregateSql(...))` on a **fresh connection outside the migration context**, so the
transactional path is exercised nowhere in the tree. That is a coverage shape worth naming: the suite tests
the *SQL* thoroughly and the *execution model* not at all. Compare TASK-246, where a feature worked in the
branch nobody used and failed in the branch everybody used, and a green suite said nothing.

Note the class remarks currently assert the opposite as a blanket guarantee — *"These statements run on the
migration's own connection and transaction, deliberately … PostgreSQL's DDL is transactional, so a migration
that fails rolls its hypertable conversion back with it."* True of the hypertable and policy emitters; not of
these two, if the hypothesis holds.

## Acceptance criteria

- [ ] **Step 0, before any fix: measure it.** Run a real migration through `TimescaleDBMigrationRunner` with
      `UseTransaction = true` (the default) that calls `CreateContinuousAggregate`, then one that calls
      `RefreshContinuousAggregate`, against live TimescaleDB. Record the exact error and SQLSTATE, or record
      that it succeeds — **the hypothesis must be falsifiable and may be false.** § Conventions (TASK-276):
      a hypothesis you cannot reproduce gets falsified or recorded, never quietly adopted.
- [ ] If it fires, the fix distinguishes the two statements rather than treating them alike: `CREATE … WITH
      NO DATA` makes creation transactional-safe at the cost of an unpopulated view, whereas a refresh cannot
      be made transactional at all and must be issued **off** the boundary. Say which mechanism each gets and
      why — the `AmbientSqlTransaction.Suppress()` precedent (TASK-243) is the shape, but note these emitters
      deliberately bypass the connector, so suppression is not directly available to them.
- [ ] `WITH NO DATA` changes observable behaviour — the view is empty until refreshed. If that is chosen,
      state it on the method and assert it, rather than letting a caller discover an empty aggregate.
- [ ] The class-level transaction remark is corrected to name the exception, in the same change.
- [ ] A test exercises these two emitters **through the runner**, not through `Exec` — otherwise the gap
      that hid this stays open for the next defect in the same place.
- [ ] Proven able to fail: the new runner-path test must go red against today's code (if step 0 confirms the
      defect) and green after, with the existing `Exec`-based tests unchanged as the control.

## Out of scope

- Routing these emitters through the connector generally so they could reuse `DoDdlCommand` and the provider
  capabilities. TASK-259 reopened that option (the `SetExternalTransaction` obstacle is gone) and recorded it
  on the class as a decision awaiting its own measurement; it is a larger behaviour change than this fix.
- The bucketing column, which **[[TASK-255]]** fixed, and the sibling's `orderByColumn` default, which
  **[[TASK-279]]** owns.

## Human test plan

- [ ] N/A — mechanical; the proof is a migration run through the real runner in its default configuration,
      which is precisely what no existing test does.
