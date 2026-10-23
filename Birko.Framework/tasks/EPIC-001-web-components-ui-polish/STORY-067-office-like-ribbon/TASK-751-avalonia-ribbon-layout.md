---
id: TASK-751
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly 2026-10-23; the human test plan is left
priority: P3
assignee: ai
created: 2026-10-23
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: equal-height groups, aligned rows, labels below, dividers halfway

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-730, TASK-743, TASK-746. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Groups one height with the divider running the full height; three equal command rows lined up across groups; each group's name centred under it; the divider halfway between groups, spaced by the group-gap token (16px); a group narrower than its name centres its commands; a compact folded group without an icon shows its initial.

## Acceptance criteria

- [x] Every group in a row is the same height; rows of commands line up across groups
- [x] Group names sit centred under their group
- [x] The divider is halfway between two groups, from a gap resource (16px default), and adds no width the scaling cannot see
- [x] A group narrower than its name centres its commands; a compact folded group without an icon shows its initial
- [x] Headless tests measure each of these

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Open the Gallery ribbon in light and dark: dividers full height and halfway, rows aligned, names below; narrow the window until groups fold, with and without group icons

## Outcome (2026-10-23)

- **Before.** Each group drew its own right border, with the whole gap after it (the mirror of the web defect
  TASK-730 fixed). Names sat straight under the commands, not on one line. Rows were plain stacks, so they drifted
  with the icons. A folded group without an icon showed ▦, the same for every group.
- **Now.**
  - Every group is a `DockPanel`, arranged at the row's height, with its name docked at the bottom.
  - Medium and Small commands sit in a `Grid` of three equal star rows, centred over the name.
  - `RibbonGroupsPanel` places a 1px divider halfway into each gap, the row's full height. These are non-focusable,
    non-hit-testable children, because `Panel.Render` is sealed. The gap is the `BRibbonGroupGap` resource: 16px
    since TASK-764, as on the web.
  - A compact folded group without an icon shows its initial in a tile; labelled, it shows its name.
- **Found while checking the screenshots.** The descenders of a group name ("p" in "Clipboard") were clipped: the line
  box is the font size, and the row clips at its own bounds. The name now has a 3px bottom margin.
- **Gate.** New `RibbonLayoutTests`, 9 tests:
  - equal heights and aligned rows; names on one line under the commands, and not clipped;
  - the divider halfway at 1100, 520 and 260px, the full height, not a stop;
  - a narrow group centred; initials on compact icon-less chunks;
  - a screenshot capture in light and dark.
  - 8 fail on the code before. Birko.Xaml.Avalonia.Tests 218/218; the Gallery builds. Checked on headless
    screenshots: light and dark at 1100, 520 and 260px, and the compact initials.


## Follow-up from your Gallery check (2026-10-23)

- **Group names cut off at the bottom, fixed.** A TextBlock clips to its own box, and with the name's line box at the
  font size its baseline falls about 1px under the box's bottom, so everything below the baseline was cut (the tail of
  Clipboard's "p", the bottoms of round letters). The 3px margin kept for the descenders was there all along; the clip
  hid them. The name is unclipped now, with no size change. The old test only checked the row's room, which is why it
  passed while the names were cut; `Group_names_are_not_clipped_by_the_row` now also checks the name's own clip and
  that the descender's ink stays in the row. Birko.Xaml.Avalonia.Tests 324/324.
