---
id: TASK-739
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly and with real keys 2026-10-22; the human test plan is left
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: toggle buttons with a pressed state

## Context

FEATURE-019 D10 (approved by the user 2026-10-22): A command can be on or off (like Bold), and shows its state.

RibbonItem.active highlights an item but is not exposed as a pressed state and does not toggle itself.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] RibbonItem can be a toggle: aria-pressed, a pressed look from tokens, click flips it and emits item-click with the new state
- [x] The app can set the state (selection changed) without an event loop
- [x] Toggle groups where only one can be on (alignment) are supported or explicitly left out
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Toggle Bold/Italic by mouse and keyboard; check the pressed look in every theme and the screen-reader state

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Decided by the user from a three-option preview:** option A, tinted and outlined. One-of sets are in scope;
  that answers FEATURE-019's open question Q4.
- **API.** `RibbonItem.toggle`, `pressed` (initial state) and `toggleGroup`. `BRibbon.setPressed(itemId, pressed,
  tabId?)` is silent; `isPressed(itemId, tabId?)` reads a state. `item-click` carries `pressed` for a toggle.
- **ARIA.**
  - A toggle has `aria-pressed`.
  - Members of a set are `role="radio"` + `aria-checked`, wrapped (consecutive members) in a
    `<span role="radiogroup" aria-label="{group label}">` with `display: contents`. The grid and the arrow-key stops
    are unchanged: the stop selectors and the flyout keys now reach into the wrapper.
- **State.** `_pressed`, keyed tab/group/item and seeded from `pressed`, is cleared by `setTabs`. The caller's objects
  are never written, and the state survives scaling re-renders. `_syncPressed` updates the rendered buttons in place.
  - Pressing a set's current member keeps it on. A disabled toggle neither flips nor reports.
  - The mobile list carries and flips the same state.
- **Look.** Accent tint plus `inset 0 0 0 1px` accent, so there is no layout shift. The focus ring adds to it.
- **Gate.**
  - Playground `ribbon-toggle-smoke` 19/19: ARIA, radiogroup and `display: contents`, stops, pressed look, clicks
    and events, disabled, set exclusivity, the caller's data untouched, `setPressed` silent and releasing others,
    `isPressed`, survival of a scaling re-render, a new `setTabs`, mobile. It fails on the TASK-738 ribbon.
  - `keyboard-check.mjs` 82/82 twice, with 3 new checks: Space and Enter flip a toggle; Space on a set member selects
    it.
  - `verify.mjs` clean; entry-points 409/409; `tsc` clean. Checked in Chrome: Bold and Columns pressed in the gallery.
