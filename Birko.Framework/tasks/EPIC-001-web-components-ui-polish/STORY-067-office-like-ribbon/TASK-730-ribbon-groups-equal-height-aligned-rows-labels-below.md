---
id: TASK-730
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly 2026-10-22; the human test plan is left
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

- [x] All groups of the open panel have one height, and every separator runs it
- [x] Rows of medium and small groups line up across groups (same row top in every group)
- [x] Group labels sit at the bottom, centred; large, medium, small and popup sizes and the flyout still read correctly
- [x] The scaling ladder still fits: ribbon-scaling-smoke and ribbon-overflow-smoke stay green, and the panel height variable still caps the panel
- [x] A Playground check measures equal group heights and aligned row tops (fails on the old layout)
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Open the Playground ribbon in light and dark: every divider runs the full height, rows line up, labels sit below
- [ ] Narrow the window step by step: groups shrink to small and to folded buttons without clipping a label

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- `.ribbon-panel-inner` is `align-items: stretch`, so every group takes the tallest one's height and each separator
  runs the full panel. The command area is `flex: 1`.
- Medium and small groups have three equal rows (`repeat(3, 1fr)`), and commands fill their row. With centred
  commands the rows still differed by 1-2px, by the icons' own heights.
- The group label stays first in the markup, so a screen reader reads the group name before its commands. It is
  drawn last (`order: 2`): centred, in sentence case, not bold.
- A folded group's button fills the group (`flex: 1 0 auto`).
- **Gate.** Playground `ribbon-layout-smoke` 14/14 at 1100, 520 and 260px. The checks are equal heights, one label
  line, labels below commands, rows within 1px, full-height folded buttons, and the label first in the markup.
  - On the old ribbon 6 of the 14 fail: heights 98/96/92/68/72/98, rows off by up to 6px, labels above.
  - Still green: `ribbon-scaling-smoke` 44/44, `ribbon-overflow-smoke` 16/16, `device-fix-check` 140/140,
    entry-points 409/409, all other suites.
  - Checked in Chrome on the Playground's own ribbon: heights 97 ×6, one label line.
- **Docs.** README, API.md and the CLAUDE.md inventory describe no label position or row sizing, so they needed no
  change, and no new i18n keys were added. Components CLAUDE.md got a Recent Updates note.
- **Found, not changed.** A folded group without an `icon` shows an empty 7px button in compact mode, on the old code
  too: compact hides the name. Filed as TASK-743. The panel also gets shorter when groups fold (98 → 26px), by the
  content-sized design. Office keeps one height; TASK-743 asks whether that should change.

## Follow-up (2026-10-22): the divider sits halfway between groups

Reported by the user from the Playground: a group's first command touched the divider, while the space after the
previous group was too big. Measured: 1px and 21px. The divider was the next group's `border-left`, and the whole flex
gap sat before it.

- **Decided by the user** from a live preview of three real ribbons: centred and tighter (Office), over centred with
  the same spacing. "I hope it uses css token": `--b-ribbon-group-gap` is now `var(--b-space-lg)` (16px), was 1.5rem.
- The divider is a `::before` drawn halfway into the gap in force (`--b-ribbon-gap-now`, which also tightens with the
  small and folded rows). It is out of flow, so it adds no width the measure pass cannot see.
- Measured after: 6.5px on each side of every divider at the Playground's 14px base.
- **Gate.** Playground `ribbon-layout-smoke` checks the divider is halfway at 1100, 520 and 260px (17/17). The check
  fails on the code before. `verify.mjs` twice; its only failure was `carousel-smoke` (TASK-745).
- **Found, not fixed here.** The measure pass reads `--b-ribbon-group-gap` with `parseFloat`, so `1.5rem` counted as
  1.5px a gap and the fold decision under-counted the row: [TASK-746](TASK-746-ribbon-measure-pass-reads-the-gap-token-as-pixels.md).

