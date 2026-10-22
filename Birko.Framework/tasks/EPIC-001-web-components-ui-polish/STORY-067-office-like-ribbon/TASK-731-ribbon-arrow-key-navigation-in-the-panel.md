---
id: TASK-731
parent: STORY-067
feature: FEATURE-019
status: verify  # verified with real key presses 2026-10-22; the screen-reader check is left
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-732, TASK-736, TASK-741, TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: arrow-key navigation between commands and groups

## Context

FEATURE-019 D2 (approved by the user 2026-10-22): Inside the open ribbon, the arrow keys move between commands and from one group to the next, the way Office does.

Today ArrowDown on a tab moves focus to the first command and Escape returns to the tabs, but nothing moves between commands: every command is its own Tab stop, so crossing a ribbon takes dozens of Tab presses.

## Acceptance criteria

- [x] Left/Right move to the previous/next command across group boundaries; Up/Down move within a column of a medium or small group
- [x] Home/End go to the first/last command of the panel; Escape returns to the active tab
- [x] The panel is one Tab stop (roving tabindex), and focus survives a re-render and a scaling change
- [x] Folded (popup) groups are one stop; Enter/Space/ArrowDown opens the flyout and arrows work inside it
- [x] keyboard-check.mjs covers it with real key presses
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Keyboard only: Tab into the ribbon, cross every group with the arrows, open a folded group, Escape back to the tabs
- [ ] NVDA or Narrator announces each command and its group

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Stops.** The panel's stops are the visible commands plus each folded group's button, under one roving tabindex.
  ↓ from a tab enters at the stop focused last. Tab leaves after one stop, and Shift+Tab comes back to it.
- **Arrows move by on-screen position** (`nearestInDirection`).
  - ←/→ go to the nearest stop that way, preferring the same row, so they cross groups at every size. They wrap at
    the ends.
  - ↑/↓ stay within the column, and ↑ from the top row returns to the active tab.
  - Home/End go to the first and last stop; Escape goes to the tab.
- **Folded groups.** Enter, Space or ↓ opens the flyout and focuses its first command, and the arrows work inside it.
  Escape closes it, and focus returns to the button.
  - The return needed a fix: when the popover closes, `activeElement` can still name the hidden command.
  - The close happens through the popover's close request, a task later, so the check waits for it. One run
    without the wait read the old focus.
- **Re-renders.** `update()` and `_showTabContent` both put focus back on the same command, for example while the
  ribbon scales.
- **Replaced, not extended.** The old ←/→ handler walked every `.ribbon-item`, including commands inside closed
  flyouts, and skipped folded groups' buttons.
- **Gate.** Playground `keyboard-check.mjs` 52/52 with 21 new ribbon checks, using real key presses, stable over
  three runs. 17 of them fail on the old ribbon.
  - `verify.mjs` stays green (ribbon layout 14/14, scaling 44/44, overflow 16/16); entry-points 409/409; `tsc`
    clean.
- **Docs.** README § b-ribbon and ACCESSIBILITY.md describe the keyboard model, and Components CLAUDE.md has a Recent
  Updates note. API.md has no ribbon keyboard section, and no new attribute or event was added.
