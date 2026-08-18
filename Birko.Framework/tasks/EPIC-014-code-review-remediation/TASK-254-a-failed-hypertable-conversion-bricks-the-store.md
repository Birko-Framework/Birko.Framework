---
id: TASK-254
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-204, TASK-243, TASK-244, TASK-472]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.TimescaleDB, Birko.Data.SQL]
---

# A hypertable conversion that cannot succeed now bricks the store instead of degrading

Spawned by Symbio **TASK-472**. Fixing the identifier defect there turned a silent no-op into a thrown
exception, which is an improvement — but it lands in the one place this repo has already decided a throw is the
wrong answer.

## What changed, and why it is now a problem

Before TASK-472, `create_hypertable` emitted a bare table literal, raised `42P01`, and
`PostgreSQLConnector.IsMissingTableException` classified that as a missing table — so `OnException` ran
`DoInit()` and returned. **Every** conversion failure was swallowed, including the legitimate ones.

With the identifier fixed, the statement reaches the server and a genuinely impossible conversion now raises
its real error. The reachable case: TimescaleDB refuses a unique index that omits the partitioning column, so a
Guid-keyed entity raises `TS103` — measured on TimescaleDB 2 / PostgreSQL 16.

That error propagates out of `TimescaleDBConnector.CreateTable`, hence out of `InitCore`, and stores set
`_initialized` only **after** schema-ensure returns. So the entity's whole surface — **reads included** —
throws on every subsequent operation. That is precisely the failure mode **TASK-204** removed for indexes:

> *Lazy schema-ensure degrades and reports; an explicit schema call throws. […] Degrade only what is a
> constraint or an optimisation — never correctness — and report rather than swallow.*

A hypertable conversion is squarely in that category: the table exists and is fully usable as a plain
PostgreSQL table without it. Partitioning is an optimisation, not correctness.

## Why TASK-472 shipped the throw anyway

Blast radius measured before shipping, per § SH-H037: **0 concrete consumer entities** and **0 framework domain
models** hit it. Symbio has `TimeSeriesRecord` with no subclass, and its `TimescaleDbStore` passes the
lowercase `"timestamp"`. So nothing breaks today, and the throw is strictly better than the silence it
replaced. It is pinned by `HypertableSchemaLiveTests.A_guid_keyed_entity_cannot_be_a_hypertable_and_now_says_so`
so the behaviour is deliberate and visible rather than accidental — but the pin records the current state, it
does not argue the state is right.

**The window closes the moment anyone declares a real time-series entity.** That is what makes this worth
filing now rather than noticing it from a production incident.

## The design question this actually opens

`AbstractConnector`'s degrade-and-report channel is index-shaped: `IndexCreationFailure` (keyed by table +
index), `IndexCreationFailures`, `OnIndexCreationFailed`, `RecordIndexCreationFailure`,
`ClearIndexCreationFailure`. Reusing it verbatim for a hypertable would mean putting a non-index failure into a
type whose name says index — so decide deliberately between:

1. **Generalise** the channel to a schema-ensure failure with a kind discriminator, keeping the existing names
   as a thin compatibility surface. Best if more non-index schema-ensure steps are coming (compression and
   retention policies are the obvious next ones).
2. **A parallel TimescaleDB-specific record**, mirroring the pattern locally. Cheaper, and honest about scope —
   but it is the second copy of a mechanism, and this epic keeps learning that the second copy is where things
   drift (TASK-245, TASK-246).

Whichever is chosen, TASK-204's invariants carry over and are not negotiable: the record is **current state
keyed by identity, not an append-only log** (connectors are cached process-wide while `_initialized` lives on
the store, so a scoped store re-runs schema-ensure per request), the event fires on the **transition** into
failure, and the record **clears** when the condition no longer holds.

## Acceptance criteria

- [ ] A hypertable conversion that cannot succeed leaves the store **initialised and usable as a plain table**,
      with the failure recorded and an event raised — not swallowed, and not fatal.
- [ ] An **explicit** `CreateHypertable` / `CreateHypertableAsync` call still throws. Same split TASK-204 drew
      and TASK-245 later narrowed: a caller asking for the conversion *now* gets the error; lazy schema-ensure
      degrades.
- [ ] The record is keyed, transition-fired and cleared when repaired — asserted, not asserted-by-construction.
      TASK-204's own regression was a list that grew one entry per HTTP request forever.
- [ ] `HypertableSchemaLiveTests.A_guid_keyed_entity_cannot_be_a_hypertable_and_now_says_so` **updated rather
      than deleted** — it currently pins the throw, and it should end up pinning the degrade plus the report.
      That test is the record of this decision changing.
- [ ] Verified against live TimescaleDB, and proven able to fail by revert.
- [ ] Decide whether the same treatment is owed to the *other* TimescaleDB schema steps (compression, retention)
      before they acquire the same shape independently.

## Human test plan

- [ ] N/A — mechanical; the proof is that a Guid-keyed entity still reads and writes after a failed conversion,
      with exactly one entry in the failure report.
