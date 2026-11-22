---
id: TASK-969
parent: EPIC-001
feature: FEATURE-023
status: done
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: a locked column shows only the lock icon

## Context

FEATURE-023 D2 (DraCode): a locked column's header shows a lock icon and a "Locked" chip, which repeats on a board with many locked columns. On every board it now shows only the icon; screen readers still hear "Locked" and pointing at it shows it. Built on `task/TASK-968` for v0.5.2.

## Acceptance criteria

- [x] A locked column's header (columns and rows layouts) shows the lock icon without the word.
- [x] The icon has the accessible name "Locked" (translatable, `bwc.kanban.locked`) and a tooltip.
- [x] Playground check; a11y-name check still passes.

## Evidence

Birko.Web `task/TASK-968` 3bcc1e1 (v0.5.2), Playground ef425d8. verify 0 failing (backport 492/492; carousel-smoke failed once, passed on the rerun); keyboard 244/244; a11y-name OK; contextual-ribbon 13/13; locale-keys 23/23; locale-switch 47/47; rerender-leak PASS; entry-points 433/433; device-fix 247/247; sortable 19/19; tree drag 17/17; escape-in-modal 27/27; theme-roundtrip 11/11; Components and Shell `tsc` clean. All from builds that printed "✓ Built".
- backport-smoke: the lock is `role="img"`, named and titled "Locked", with no word in it; a11y-name OK. Chrome: the gallery's Done header reads only the icon.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22 in the Playground: "all passed".
Merged into Birko.Web main fcf0623, tagged v0.5.2 (not pushed).

## Human test plan

1. In the gallery's kanban, the locked Done column's header shows a lock icon without the word "Locked".
2. Point at the icon: a "Locked" tooltip.
