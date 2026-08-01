---
id: FEATURE-016
created: 2026-07-06
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko framework backports from Reps (+ cross-provider & Xaml follow-ups)

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-016](../../../tasks/EPIC-016-birko-backports-from-reps/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Building the **Reps** workout-tracker consumer (`Consumers/WorkoutTracker`) surfaced generic
capability that had to be written app-side because the framework didn't provide it yet. The
app-agnostic pieces were moved **upstream** into `Birko.Framework` / `Birko.Web`, one framework
ergonomics bug was fixed, and a couple of places where the app reinvented something Birko already
ships were swapped back to the framework's own helpers.

This epic is the framework-side tracking home for that work. It was **migrated from
`Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports`** — the backport work was originally
tracked in the consumer as a pragmatic home before the framework's own `tasks/` tree was in play.
The two DONE stories (backend + frontend backports) are re-homed here as **completed ledgers**
(full per-task detail stays in the Reps tree, referenced per row); the Reps-side *adoption*
cleanups (swapping local copies for the upstream versions) remain in Reps as
`EPIC-002 / STORY-010` since that is app work.

A follow-up **backport review (2026-07-06)** then asked which of the shipped backports still need
replicating to sibling providers or to the parallel Xaml UI family. Its findings became the new,
actionable stories below (STORY-039/040/041).

A second kind of finding then showed up, which the ledger stories cannot hold: not "the framework is
missing this", but "the framework has this, a consumer adopted it, and the adoption found what it
gets wrong". Those live in **STORY-052**, which stays open for the life of the epic — the shipped
ledgers (STORY-037/038) are done and are not reopened for them.

> **Migration reference:** ⇄ `Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports`
> (framework stories 008/009 → this epic; STORY-010 stays in Reps).

## Proposed shape

- Each shipped backport is consumable by a fresh Birko project without app-specific glue (met —
  see STORY-037/038 ledgers).
- The store-factory + DI ergonomics SQLite gained are available for the other SQL providers, so no
  SQL consumer hand-wires stores per app (STORY-039).
- The generic UI/offline/device capabilities that landed in `Birko.Web.*` have Xaml analogues where
  a Xaml/Avalonia app plausibly needs them — or a recorded decision that they are web-only
  (STORY-040).
- `BMobileAppShell` is discoverable and exercised in the reference surfaces (Playground + Gallery),
  not just shipped as library code (STORY-041).
- A component gap a consumer hits while adopting the catalogue is fixed **in the component**, with no
  consumer left on a fork and no consumer-specific branch upstream (STORY-052).
- Nothing workout-domain-specific leaks upstream.

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Helpers, Birko.Communication.AspNetCore, Birko.Data.Migrations.SQL, Birko.Data.SQL.SqLite, Birko.Data.SQL.MSSql, Birko.Data.SQL.MySQL, Birko.Data.SQL.PostgreSQL, Birko.Web.Core, Birko.Web.Components, Birko.Web.Shell, Birko.Xaml.Core, Birko.Xaml.Shell, Birko.Xaml.Avalonia]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
