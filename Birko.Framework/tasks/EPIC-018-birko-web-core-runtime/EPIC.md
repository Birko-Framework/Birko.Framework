---
id: EPIC-018
status: in-progress
created: 2026-08-17
owner: ai
affects: [Birko.Web.Core]
source: DV5 orphan analysis 2026-08-17
---

# Birko.Web.Core — the browser-side runtime

## Area of concern

`Birko.Web.Core` is the framework's browser-side runtime: the HTTP client, the offline
outbox and sync manager, the local mirror, routing, and the token/theme layer that
`Birko.Web.Components` and `Birko.Web.Shell` build on. **It had no epic**, and this one
exists because that absence was producing orphans rather than because a new initiative
started.

**How it was found.** [[roadmap]]'s DV5 flagged 33 tasks in `tasks/_loose/` with no epic
and no feature. Twelve were framework Data/SQL defects and were re-homed to EPIC-014. Four
were `Birko.Web.Core` defects with nowhere to go, and checking why produced this:

- `Birko.Web.Core` appears in the `affects:` list of **four** epics — EPIC-011 (test
  coverage gaps), EPIC-013 (reference consumers), EPIC-015 (Xaml, which mirrors Web) and
  EPIC-016 (backports from Reps). **None of them is about it.** Being named as *affected*
  is not the same as being owned.
- EPIC-001 is `Birko.Web.Components`; all six of its stories are component work
  (`b-range`, `b-form`, the editable table, the help-text row). Web.Core is not in it.
- EPIC-016's STORY-052 was the closest candidate and is explicitly the wrong shape: it
  covers *"component gaps found by consumers adopting the `b-*` catalogue"*, and its
  acceptance is *"the fix lives in the component or it is not a fix."* An HTTP timeout and a
  sync-manager replay are not component fixes.

**Why the absence is not cosmetic.** Two of the four inherited tasks are **tracking
backfills** — fixes that landed in `Birko.Web.Core` (`79e786e` on 2026-07-31, `0914ca1` on
2026-08-02) with no aggregator commit, no task, no `pr:` sha and no spec regen, found only
by diffing the sibling repo's `git log` against this one. That is what happens when work has
nowhere to be filed: it ships untracked, and the third commit of the polyrepo model simply
does not get made.

## Requirement → feature traceability

| Requirement | Feature |
|---|---|
| The browser-side runtime is tracked, so a Web.Core fix has a home and gets its aggregator commit | [FEATURE-018](../../docs/features/FEATURE-018-birko-web-core-runtime/idea.md) |

## Stories

Currently none — the inherited work is tracked directly on the epic, the same shape as
EPIC-014's D11. Stories get created when a theme has more than one task.

## Out of scope

- `Birko.Web.Components` (EPIC-001) and the `b-*` catalogue's adoption gaps (EPIC-016
  STORY-052). This epic is the runtime underneath them.
- `Birko.Web.Shell`. Not currently orphaned; if it starts producing homeless tasks, widen
  this epic deliberately rather than by accident.
- The **Xaml** mirror of these capabilities (EPIC-015). A defect fixed here may well need
  replicating there, but that is EPIC-015's story to carry.
- TASK-130 (theme colour-contrast gating). Deliberately left in `_loose`: it spans
  `Birko.Web.Components` **and** `Birko.Xaml.Avalonia`, so it belongs to EPIC-001 or
  EPIC-015 rather than here, and guessing between them would bury it somewhere less visible
  than `_loose`.
