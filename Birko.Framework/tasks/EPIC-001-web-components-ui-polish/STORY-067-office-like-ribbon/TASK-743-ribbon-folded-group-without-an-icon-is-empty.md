---
id: TASK-743
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly 2026-10-22; the human test plan is left
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-730]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: a folded group without an icon is an empty 7px button

## Context

Found while building TASK-730. When the ribbon is narrow enough that groups fold into one button each and the row
is `compact`, the button drops its group name to save width. A group declared without `icon` then has nothing to
show: measured at 260px, six such buttons were 7px tall and empty. It was the same on the code before TASK-730.
The name stays in `title` / `aria-label`, so it is named for screen readers, but sighted users see nothing to press.

The panel also gets shorter as groups fold (98px with medium groups → 26px folded, with icons), because the panel
is content-sized by design (STORY-049). Office keeps one ribbon height, so the commands below never jump.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] A folded group without an icon still shows something to press in compact mode: a fallback glyph, its
      initial, or compact is not applied to it (decide from a preview)
- [x] Decide, with the user, whether the open panel keeps one height while groups fold (Office) or stays
      content-sized (today); if it changes, the scaling suites still pass
- [x] `ribbon-layout-smoke` covers an icon-less folded group

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Narrow the Playground ribbon until every group folds, with and without group icons: every button is visible
      and pressable

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Decided by the user** from a live preview of real ribbons at 260px:
  - An icon-less compact folded group shows its group's initial in a small tile, over one generic glyph for all. A
    third option, keeping the name, was dropped from the preview: compact exists because named buttons do not fit
    the row, so they would be clipped.
  - The panel stays content-sized (today), over Office's one height.
- **Built.** The initial is the label's first letter, markup stripped and escaped, and it shows only in compact mode.
  The name stays the button's accessible name and tooltip. A group with an icon is unchanged.
- **Gate.** Playground `ribbon-layout-smoke` 19/19, with two new checks: six icon-less groups at 260px each show
  their initial on a 25px button, and the initial is text, never markup. On the code before they fail with the
  defect as filed: six empty 7px buttons. `verify.mjs` clean twice; keyboard-check 195/195; `tsc` clean. Checked in
  Chrome, mixed with a group that has an icon.

