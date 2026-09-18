---
id: FEATURE-018
created: 2026-08-17
---

# Birko.Web.Core — the browser-side runtime — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Track the browser-side runtime as its own area of concern | approved | Four fixed defects had no feature and were invisible to every stakeholder view; two had shipped with no tracking commit at all. Being listed in four epics' `affects:` is not ownership. | 2026-08-17 | user | [[TASK-198]], [[TASK-199]], [[TASK-202]], [[TASK-203]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-17 — **Ledger opened to close a tracking gap, not to start an initiative.** [[roadmap]]'s DV5
  flagged 33 parentless tasks; 12 were re-homed to EPIC-014 and 4 were `Birko.Web.Core` defects with
  nowhere to go. Checking why found that Web.Core appears in **four** epics' `affects:` lists
  (EPIC-011/013/015/016) while none of them is *about* it, EPIC-001 is `Birko.Web.Components`, and
  EPIC-016's STORY-052 is explicitly component-shaped (*"the fix lives in the component or it is not a
  fix"*). An HTTP timeout is not a component fix.
- 2026-08-17 — **What D1 does claim:** the four tasks are real, already fixed, and now visible to a
  stakeholder view. `By` is `user`, because creating an epic and feature is scope creation and was an
  explicit choice rather than a backfill inference.
- 2026-08-17 — **What it does not claim:** no roadmap, no story breakdown, and no assertion that these
  four are the whole of Web.Core's outstanding work. They are the four that were homeless. The epic
  carries no stories yet by design — a story is created when a theme has more than one task.
- 2026-08-17 — **The absence had a measurable cost, which is the argument for the row.** [[TASK-202]] and
  [[TASK-203]] are *tracking backfills*: fixes that landed in `Birko.Web.Core` on 2026-07-31 (`79e786e`)
  and 2026-08-02 (`0914ca1`) with no aggregator commit, no task, no `pr:` sha and no spec regen. Both were
  found only by diffing the sibling repository's `git log` against this one. Work with nowhere to be filed
  ships untracked; that is the pattern this feature exists to stop, not merely a bookkeeping tidy.
- 2026-08-17 — **[[TASK-130]] deliberately excluded.** Theme colour-contrast gating spans
  `Birko.Web.Components` **and** `Birko.Xaml.Avalonia`, so it belongs to FEATURE-001 or FEATURE-015.
  Left in `tasks/_loose/` rather than filed here, because a wrong home is less visible than no home.
