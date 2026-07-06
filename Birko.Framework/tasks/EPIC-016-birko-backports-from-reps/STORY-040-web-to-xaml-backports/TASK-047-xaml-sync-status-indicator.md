---
id: TASK-047
parent: STORY-040
feature: null
status: done
priority: P3
assignee: ai
created: 2026-07-06
depends-on: [TASK-046]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Xaml sync-status indicator (offline / syncing / synced)

## Context

`Birko.Web.Components` gained `<b-sync-status>` from Reps (STORY-038 / origin TASK-037): a chip bound
to an `ActionQueue` rendering offline / syncing / synced. The Web→Xaml review found Xaml has
indicator controls (`BBadge` / `BTag` in `Indicators.axaml`) but **no sync chip** and — more
fundamentally — **no ActionQueue / outbox** for one to bind to. So this is worth doing only *after*
a Xaml offline queue/mirror exists (TASK-046); on its own it has nothing to display.

Blocked-by-design on TASK-046 (`depends-on`).

## Acceptance criteria

- [x] A sync-status control in `Birko.Xaml` that renders offline / syncing / synced from a bindable
      sync-source state. — `Controls/SyncStatusIndicator.cs` (`ContentControl` with a `Status` styled property; status class + token foreground).
- [x] Binds to the queue/mirror state introduced by TASK-046, updating reactively. — `Status` binds to `MirrorDataSource<T>.Status` (`SyncStatus`); updates on property change.
- [x] States are localized via the existing `I18n`. — keys `bxaml.sync.{synced,syncing,offline}` via `I18n.Instance`, English fallback.
- [x] Tests demonstrating the three states. — `SyncStatusIndicatorTests` (content + class per state; renders hosted in a window).
- [x] `Recent Updates` entry added.

## Out of scope

- The queue/mirror itself (TASK-046).

## Human test plan

- [x] Drive the sync-source through synced → syncing → offline and confirm the indicator reflects each
      state. — `SyncStatusIndicatorTests` asserts content + status class for all three; `MirrorDataSource` drives `Status`.

## Implementation plan

_Populated by `/tasks plan TASK-047` — leave empty until then._
