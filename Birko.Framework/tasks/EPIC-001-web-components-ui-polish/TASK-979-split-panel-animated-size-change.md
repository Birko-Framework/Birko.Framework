---
id: TASK-979
parent: EPIC-001
feature: null
status: verify
priority: P3
assignee: ai
created: 2026-11-24
depends-on: []
blocks: []
related: [TASK-525]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-split-panel`: a size change can animate (opt-in)

## Context

Asked for by DraCode (its TASK-058, 2026-11-24). DraCode's workspace puts its tabs and its chat panel in a
`b-split-panel` and widens the chat panel while it has focus by setting `sizes`. Its own grid used to animate that
with `transition: grid-template-columns 150ms ease` (none under reduced motion); the grid now lives in the panel's
shadow root, so the app cannot style it.

## Acceptance criteria

- [x] `.split` has `transition: grid-template-columns var(--b-split-transition, 0s)`: default 0s, so no existing app
      changes; an app sets e.g. `b-split-panel { --b-split-transition: 150ms ease }`.
- [x] A dragged separator does not lag: no transition while a pointer drag is in progress, nor on its drop (the
      `.split` carries a `resizing` class from pointerdown until the drag's width is committed). Keyboard resizing
      animates like any other size change.
- [x] `prefers-reduced-motion: reduce` turns it off in the component, even when the app set the variable.
- [x] The doc comment, README and CHANGELOG mention the variable.

## Evidence

Birko.Web `task/TASK-979` 44471c1 (joins the unpushed v0.5.3), Playground 04621e3. All from builds that printed
"✓ Built".
- `split-transition-check` 10/10 (computed styles, a real mouse drag): off by default; opted in, a `sizes` change is
  mid-way at 120 ms of 400 ms (361 px of 300→500) and lands after; during a drag the transition is 0s and the column
  follows the pointer at once, and the drop is not animated; the transition is back after; reduced motion wins.
  **Proven red:** 7/10 against Birko.Web main, and 8/10 with only the drag guard removed (the column stayed at 640 px
  during a 150 px drag).
- verify 0 failing; keyboard-check 244/244; rerender-leak PASS; entry-points 433/433; theme-roundtrip 11/11;
  Components `tsc` clean.

## Out of scope

- Animating the responsive collapse (`collapse-at`) between one and two columns.

## Human test plan

- [ ] In DraCode, after Birko.Web main has it: focusing the chat panel widens it smoothly and leaving it narrows it
      smoothly; dragging the divider follows the pointer without lag; with Windows "Show animations" off (reduced
      motion) the width changes at once.

**Merged 2026-11-24** on the user's word ("merge") into Birko.Web main 3e56111 as part of v0.5.3 (the unpushed tag
moved to the merge); `split-transition-check` 10/10 and verify 0 failing on main. Stays at verify until the DraCode
step above is run.
