---
id: TASK-730
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-733, TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: equal-height groups, aligned rows, group labels at the bottom

## Context

FEATURE-019 D1 (approved by the user 2026-10-22): Every group in the open ribbon is the same height, so the dividers between groups run the full height; commands sit in three rows that line up across groups; each group's name sits centred underneath it, as in Office.

Measured in the Playground on 2026-10-22: the six Home-tab groups were 97, 95, 94, 68, 71 and 97px tall, so their separators (each group's border-left) ended at different heights. `.ribbon-panel-inner` lays groups out with `align-items: flex-start`, and the medium/small grids use `grid-template-rows: repeat(3, auto)`, so rows drift with icon sizes. Group labels are uppercase captions above the commands. The user picked the full variant from a live CSS preview: `align-items: stretch`, three fixed-height rows, label last and centred, sentence case.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [ ] All groups of the open panel have one height, and every separator runs it
- [ ] Rows of medium and small groups line up across groups (same row top in every group)
- [ ] Group labels sit at the bottom, centred; large, medium, small and popup sizes and the flyout still read correctly
- [ ] The scaling ladder still fits: ribbon-scaling-smoke and ribbon-overflow-smoke stay green, and the panel height variable still caps the panel
- [ ] A Playground check measures equal group heights and aligned row tops (fails on the old layout)
- [ ] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Open the Playground ribbon in light and dark: every divider runs the full height, rows line up, labels sit below
- [ ] Narrow the window step by step: groups shrink to small and to folded buttons without clipping a label
