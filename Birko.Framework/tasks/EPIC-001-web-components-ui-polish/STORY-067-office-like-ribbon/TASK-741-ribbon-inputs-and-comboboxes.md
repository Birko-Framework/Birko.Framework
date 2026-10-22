---
id: TASK-741
parent: STORY-067
feature: FEATURE-019
status: verify  # verified with real clicks and keys 2026-10-22; the human test plan is left
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-731]
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: inputs and comboboxes inside the ribbon

## Context

FEATURE-019 D12 (approved by the user 2026-10-22): A group can hold a small input or dropdown (like Office's font name and size boxes).

Groups hold buttons only.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] RibbonItem can be a text input, a number input or a select (reusing b-input / b-select bare, size sm)
- [x] Its value is reported with an event; arrow keys inside it edit, Tab or Escape leave it
- [x] Scales and folds with its group
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Type into a ribbon input and pick from a ribbon dropdown by keyboard; narrow the window and check it folds

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Decided by the user.** All three previewed caption looks kept and switchable: `field-caption` none (default) /
  beside / above, overridable per box. Free values per dropdown (`freeText`), over always or never.
- **API.**
  - `RibbonItem.field` = `{ type: 'text' | 'number' | 'select', value?, options?, freeText?, placeholder?, min?, max?,
    step?, width? (characters, 8), caption? }`.
  - `item-change` = `{ tabId, groupId, itemId, value }`.
  - `setFieldValue(itemId, value, tabId?)` is silent; `getFieldValue(itemId, tabId?)` reads.
  - b-select gets `no-clear`.
- **Behaviour.**
  - Committing: Enter, a plain dropdown's pick, or leaving the box. The same value is not reported twice.
  - Keys: an arrow moves into a box; inside, the arrows, Home and End edit it. Tab leaves the ribbon. Escape puts the
    reported value back and returns to the tab (not when it only closed the box's own list).
  - What was typed survives a re-render.
  - An unpinned ribbon stays open while a box has focus, and closes after an explicit commit.
  - A folded group's flyout holds its boxes and closes on Enter.
  - Key tips focus the box with its text selected. The hover tooltip names the box, but not while it is typed in.
  - The mobile list holds the boxes.
  - Boxes are not offered to the Quick Access Toolbar.
- **i18n.** No new keys: a box is named by its own label.
- **Fixed while building.** b-select's clear (×) hid a three-character value: new `no-clear`. b-select sent `change`
  while the ribbon filled in its choices, and those leaked out of the ribbon; the ribbon now swallows them.
- **Flake.** Playground `ribbon-scaling-smoke` failed 1 run in 6 again: another suite's auto popover closes its
  flyout, not only a modal. It now retries while the flyout did not stay open. Clean for the next six runs.
- **Found, not fixed.** `carousel-smoke` failed in 2 of 10 `verify.mjs` runs, in suites this task does not touch.
  Spawned [TASK-745](../TASK-745-carousel-smoke-flaky-under-verify.md).
- **Gate.** Playground `keyboard-check.mjs` 195/195, with 27 new input checks using real keys and clicks. They crash
  on the TASK-740 ribbon (no boxes). entry-points 409/409; `tsc` clean. Checked in Chrome: all three caption looks
  in the Playground ribbon's Font and Size groups.
