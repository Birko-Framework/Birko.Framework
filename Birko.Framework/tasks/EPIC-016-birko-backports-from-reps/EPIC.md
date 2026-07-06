---
id: EPIC-016
status: in-progress
created: 2026-07-06
owner: ai
affects: [Birko.Helpers, Birko.Communication.AspNetCore, Birko.Data.Migrations.SQL, Birko.Data.SQL.SqLite, Birko.Data.SQL.MSSql, Birko.Data.SQL.MySQL, Birko.Data.SQL.PostgreSQL, Birko.Web.Core, Birko.Web.Components, Birko.Web.Shell, Birko.Xaml.Core, Birko.Xaml.Shell, Birko.Xaml.Avalonia]
---

# Birko framework backports from Reps (+ cross-provider & Xaml follow-ups)

## Area of concern

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

> **Migration reference:** ⇄ `Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports`
> (framework stories 008/009 → this epic; STORY-010 stays in Reps).

## Requirement → landing map (shipped)

| Backport | Landed in | Origin (Reps) |
|---|---|---|
| `MapOwnedCrud<…>()` owner-scoped minimal-API CRUD | `Birko.Communication.AspNetCore/OwnedCrud/` | TASK-030 |
| Name-only enum parse + empty-Guid normalize | `Birko.Helpers/EnumHelper.cs`, `GuidHelper.cs` | TASK-031 |
| Mapping-driven "create tables" migration | `Birko.Data.Migrations.SQL/CreateTablesMigration.cs` | TASK-032 |
| SQLite store-factory + DI extension | `Birko.Data.SQL.SqLite/{Stores,Extensions}/` | TASK-033 |
| SQLite migration transaction default + observable failure | `Birko.Data.Migrations.SQL/{SqlMigrationSettings,SqlMigrationRunner}.cs` | TASK-034 |
| PWA service worker + precache/versioning scaffold | `Birko.Web.Core/pwa/` | TASK-035 |
| MirrorStore / readThrough offline read cache | `Birko.Web.Core/mirror-store.ts` | TASK-036 |
| `<b-sync-status>` chip | `Birko.Web.Components` | TASK-037 |
| `BMobileAppShell` (top-bar + bottom-nav, `Surface[]`) | `Birko.Web.Shell` | TASK-038 |
| Screen Wake Lock manager | `Birko.Web.Core` | TASK-039 |
| iOS-safe audio cue + priming | `Birko.Web.Core/audio-cue.ts` | TASK-040 |
| `Formatter.duration()` + Intl passthrough | `Birko.Web.Core` | TASK-041 |

## Success criteria

- Each shipped backport is consumable by a fresh Birko project without app-specific glue (met —
  see STORY-037/038 ledgers).
- The store-factory + DI ergonomics SQLite gained are available for the other SQL providers, so no
  SQL consumer hand-wires stores per app (STORY-039).
- The generic UI/offline/device capabilities that landed in `Birko.Web.*` have Xaml analogues where
  a Xaml/Avalonia app plausibly needs them — or a recorded decision that they are web-only
  (STORY-040).
- `BMobileAppShell` is discoverable and exercised in the reference surfaces (Playground + Gallery),
  not just shipped as library code (STORY-041).
- Nothing workout-domain-specific leaks upstream.
