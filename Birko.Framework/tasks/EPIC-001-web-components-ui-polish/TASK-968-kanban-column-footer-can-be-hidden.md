---
id: TASK-968
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

# b-kanban: a board can hide the column footer

## Context

FEATURE-023 D1 (DraCode): the "N cards" footer under each column repeats the header count. A board setting hides it; boards that do not set it keep it, so Symbio's boards do not change. Built on `task/TASK-968` for Birko.Web v0.5.2.

## Acceptance criteria

- [x] `showColumnFooter: false` in the board configuration hides every column's footer, in columns and stacked layouts.
- [x] Without it the footer shows as before.
- [x] Playground check.

## Evidence

Birko.Web `task/TASK-968` 3bcc1e1 (v0.5.2), Playground ef425d8. verify 0 failing (backport 492/492; carousel-smoke failed once, passed on the rerun); keyboard 244/244; a11y-name OK; contextual-ribbon 13/13; locale-keys 23/23; locale-switch 47/47; rerender-leak PASS; entry-points 433/433; device-fix 247/247; sortable 19/19; tree drag 17/17; escape-in-modal 27/27; theme-roundtrip 11/11; Components and Shell `tsc` clean. All from builds that printed "✓ Built".
- backport-smoke: the footer shows by default (2 columns) and `showColumnFooter: false` hides it (0).

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22 in the Playground: "all passed".
Merged into Birko.Web main fcf0623, tagged v0.5.2 (not pushed).

## Human test plan

1. In the Playground gallery's kanban, switch "column footer" off: the "N cards" lines under the columns go. Switch it on: they come back.
