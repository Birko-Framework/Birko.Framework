---
id: TASK-743
parent: STORY-067
feature: FEATURE-019
status: todo
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

- [ ] A folded group without an icon still shows something to press in compact mode: a fallback glyph, its
      initial, or compact is not applied to it (decide from a preview)
- [ ] Decide, with the user, whether the open panel keeps one height while groups fold (Office) or stays
      content-sized (today); if it changes, the scaling suites still pass
- [ ] `ribbon-layout-smoke` covers an icon-less folded group

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Narrow the Playground ribbon until every group folds, with and without group icons: every button is visible
      and pressable
