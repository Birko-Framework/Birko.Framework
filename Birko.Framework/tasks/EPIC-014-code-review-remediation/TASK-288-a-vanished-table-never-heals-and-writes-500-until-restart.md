---
id: TASK-288
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
priority: P2
assignee: unassigned
created: 2026-08-31
depends-on: [TASK-277, TASK-285, TASK-286, TASK-287]
blocks: []
findings: []
pr: 0fa20ab (Birko.Data.SQL) + 8d75267 (Birko.Data.Stores) + c34a278 (Birko.Data.SQL.SqLite.Tests)
github-issue: null
jira-key: null
---

# A table that vanishes under an initialised store never comes back — every write 500s until restart

## Context

The framework half of **Symbio TASK-627**, which found this while proving TASK-602's instrument could
fire. It is **not** TASK-602's mechanism — that question stays open there — it is a separate defect the
experiment exposed, and it contradicts a documented contract.

`AbstractConnector.EnsureSchemaAndReport` documented itself, and TASK-277 justified it, as: *rethrow so
this attempt is reported, but call `DoInit()` **so the next attempt can succeed***. The first half held.
**The second never did.**

**Measured at the framework level before a line changed** (SQLite, `AsyncSQLiteStore<T>`), reproducing
Symbio's live-API table exactly. The condition was forced rather than waited for: write once so the store
initialises and the table is created, then `DROP TABLE` beneath the running store, so `_initialized` stays
`true` while the table is gone.

| step | result |
|---|---|
| after seed | `sqlite_master` = **1** |
| after `DROP TABLE` | 0 |
| write #1 | **threw**, `sqlite_master` = 0 |
| writes #2–#5 | **threw**, `sqlite_master` = **0 throughout** |
| `CountAsync` on the same table | **0, no error** |
| a fresh store instance (= process restart) | table recreated, `sqlite_master` = 1 |

### Why `DoInit()` was never going to do it

- `DoInit()` raises the `OnInit` event, and **nothing in the framework subscribes to it** — only a
  consumer can, through `IDataBaseRepository.AddOnInit`. TASK-277 had already written that down about the
  *swallow*; nobody carried it forward to the *promise* in the very next paragraph.
- It is connector-level and issues **no per-entity DDL**. Even a subscriber would have to know which
  entity to recreate.
- And it could not have mattered anyway: the store's remembered `_initialized` short-circuits
  `EnsureInitializedAsync` **before** `InitCore` is reached, so schema-ensure never ran again. The restart
  row is what proves the broken state was in memory rather than on disk.

⚠ **Reads hid all of it, which is what made it dangerous.** The same dropped table answered a count with
**0 and no error** — correct behaviour by TASK-211/TASK-285 — so the surface looked healthy while every
write failed, and a monitor watching reads saw nothing.

## The decision, and its cost

**Decided: heal, and record every heal.** Taken explicitly rather than by default, because TASK-627 flags
that healing removes a discriminator Symbio TASK-602 was reasoning from.

The framework's own recorded principle points one way and is quoted here because it is what settled it —
`AbstractStore.CanRememberInitialization`:

> Answering "no" costs one idempotent re-run, and that asymmetry is the whole design. A false negative
> re-issues `CREATE TABLE IF NOT EXISTS`; a false positive leaves a store permanently broken for the life
> of the process. So this errs toward re-running.

That is exactly the outcome measured above, so the same asymmetry answers the same way.

⚠ **The cost, stated plainly.** TASK-602 Round 4 argued: *because a real absence never heals, and because
both observed occurrences healed on the very next call, the observed anomaly is provably not an absent
table.* **That inference no longer holds after this change.** It is affordable only because the heal now
announces itself: every invalidation writes to `AbstractConnector.SchemaEscapes` and raises
`OnSchemaEscapeDetected` (TASK-287's channel), so an absent table is **recorded** rather than inferred
from a symptom — a strictly stronger signal than the one withdrawn. The recording is therefore not a
nicety here, it is the precondition, and it has its own assertions.

⚠ **Symbio still has to subscribe.** Symbio surfaces connector diagnostics only through boot-time checks,
which by construction cannot see a runtime escape. Until it subscribes to `OnSchemaEscapeDetected`, the
replacement signal exists in the framework and reaches no log — the same open Symbio half TASK-287 has.

## What was implemented

- **`AbstractConnector.SchemaGeneration`** — a counter bumped when this connector sees a table **it
  created** being reported missing. Bumped in `RecordSchemaEscape`, one statement from the record, because
  they are the same event.
- **`AbstractStore` / `AbstractAsyncStore.CanTrustRememberedInitialization`** — default `true`, consulted
  in both the outer fast path and the inner double-check of the init gate. `CanRememberInitialization`'s
  other half: that one asks *may I remember this?* at init time, this asks *does what I remembered still
  hold?* at use time.
- **The two SQL stores override it**, comparing the generation they pinned at the end of `InitCore`.
- **The detection moved into `EnsureSchemaAndReport`** and TASK-287's two count-path call sites were
  removed — see the supersession note on that task.

Four things are deliberate:

- **A pull, not an event.** Connectors are cached process-wide per (type, settings id) while a web app
  resolves a store per request, so a subscriber list on the connector accumulates dead stores on a
  process-lifetime object. That is TASK-204's defect arriving through a different door. A counter read
  costs nothing and cannot leak.
- **Gated on the anomaly, never on "a table was missing".** Ordinary lazy first-touch is also a missing
  table and is ~245× more common per bring-up; reacting to it would re-run schema-ensure for every store
  on the database, hundreds of times per start-up. The gate is TASK-286's `_tablesCreated` record — which
  is therefore **no longer "diagnostic only, nothing branches on it"**, and that comment was corrected
  rather than left to mislead.
- **The failing attempt still throws.** Healing must not buy recovery back by going quiet about the
  operation that was lost (TASK-277). Asserted.
- **Captured after `CreateTable`, not before.** An escape seen while our own schema-ensure was running has
  just been addressed by it; treating that as staleness would re-run forever.

## Acceptance criteria

- [x] Named: why `DoInit()` does not restore the table. *(Three reasons, all verified in source and by the
      probe — the filed hypothesis was right about the store's gate and incomplete about the rest.)*
- [x] Decided whether it *should* heal, deliberately, with the consequence written down.
- [x] `EnsureSchemaAndReport`'s doc comment stops promising *"so the next attempt can succeed"* on
      `DoInit()`'s account, and now says what actually delivers it.
- [x] ⚠ Coordinate with **TASK-602** before landing. *Partly discharged: this file records the
      measurement and names the withdrawn inference so the reasoning survives the behaviour change. The
      note still has to be carried into TASK-602 itself, which is Symbio's side.*
- [x] Regression test at the framework level: initialise a store, remove the table beneath it, assert the
      decided behaviour.

## Out of scope

- **TASK-602's mechanism** — why a live statement could not see a table that existed. Different question,
  still open.
- **The count path returning `0`** for a missing table — TASK-285, deliberate, verified unchanged here and
  asserted, because it is the half that hid this defect.
- **The host-side subscription** that turns the record into a log line — Symbio's, shared with TASK-287.
- **Non-SQL backends.** `CanTrustRememberedInitialization` defaults to `true`, so this is a no-op for
  every store where nothing can remove the schema underneath it. No backend other than SQL was measured
  to have the equivalent condition, and none is claimed to.

## Human test plan

- [x] N/A — mechanical. The verdict is whether a write succeeds and whether `sqlite_master` holds a row,
      both asserted, and `sqlite_master` is read on a connection of its own.

## Outcome (2026-08-31)

Landed as `0fa20ab` (`Birko.Data.SQL`) + `8d75267` (`Birko.Data.Stores`) + `c34a278`
(`Birko.Data.SQL.SqLite.Tests`). After the fix the same forced condition gives: write #1 throws (still
reported), **write #2 succeeds and the table is back**, #3 onward normal.

Verified with `BIRKO_REQUIRE_LIVE` set throughout: `Birko.Data.SQL.SqLite.Tests` **277 passed, 0 failed,
0 skipped** (271 → 277), plus `Birko.Data.SQL` 655, `Birko.Data.Tests` 205, InMemory 69, Core 86, Views 36,
JSON 23, XML 18, Repositories 16, Tenant 68, Tagging 20, SqLite.View 9, Composition 21, and the SQL-backed
Workflow / BackgroundJobs / Outbox / Sync / Caching suites — all green.

Three mutations, disjoint:

| mutation | red | which |
|---|---|---|
| store never distrusts its remembered init | 2 | both healing tests; **every observability test stays green**, so the halves are independent |
| heal the async store only | 1 | the sync one — the two bases keep separate gates |
| bump the generation for every missing table | 1 | the benign first-touch pin, which is what stops the fix re-initialising hundreds of times per start-up |

Three things worth carrying:

- **A recovery branch that neither repairs nor retries is only a swallow — and TASK-277 wrote that down
  about the swallow while leaving the identical claim standing one paragraph later as a promise.** Read
  what a recovery call *does*, then read what the sentence next to it *claims*.
- **The state that was wrong lived in a different class from the code that detected the failure.** The
  connector saw the missing table; the flag that had to change was the store's. Fixing it where it was
  detected (the connector) was impossible, and subscribing across the gap would have reintroduced
  TASK-204's leak. A counter pulled at the gate is what bridges a process-wide object and a per-request
  one without either owning the other.
- **⚠ The two halves of this change are independent, and only a mutation shows it.** Healing without
  recording passes every observability test; recording without healing passes every healing test. They
  are shipped together because the recording is the *licence* for the healing, not because one implies
  the other — so a later change that quietly drops the recording would leave a heal that costs TASK-602
  its discriminator and returns nothing.

## Notes for whoever picks up Symbio TASK-627

The framework half is done and the consumer half is not, in two places:

1. **Subscribe to `AbstractConnector.OnSchemaEscapeDetected`** (or read `SchemaEscapes`) from somewhere
   that runs at request time, not from a boot-time check. Without this, neither TASK-287 nor this task is
   visible in production.
2. **Carry the withdrawn inference into TASK-602** before its Round 4 reasoning is cited again.

## Implementation plan

_Not populated — the mechanism was measured first and the design followed from the measurement._

## Spawned

- **[[TASK-289]] (P1)** — a throwing `OnSchemaEscapeDetected` subscriber replaces the annotated exception,
  which destroys TASK-286's diagnostic on the write path and, because the count path's exception filter
  then stops matching, makes `SelectCount` **throw instead of returning 0** — TASK-285 reopened from
  outside the framework. Measured at this task's close. TASK-288's heal survives (record and increment both
  happen before the `Invoke`), which is ordering rather than design and is part of what TASK-289 must pin.
  ⚠ Rated P1 because the "Notes for whoever picks up Symbio TASK-627" section above tells someone to write
  the handler that triggers it, and because the channel has **zero** consumers today — so it can be
  hardened for free right now, exactly as TASK-254 hardened the hypertable channel, and that window shuts
  on the first subscriber.
