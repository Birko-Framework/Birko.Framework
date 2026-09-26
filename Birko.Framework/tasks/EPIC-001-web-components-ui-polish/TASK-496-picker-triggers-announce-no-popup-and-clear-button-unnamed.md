---
id: TASK-496
parent: EPIC-001
feature: FEATURE-001
status: todo
priority: P2
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Picker inputs don't announce their popup, and `b-select`'s clear button has no name

## Context

Found by the owner on 2026-09-26 during the screen-reader passes for [[TASK-489]] and [[TASK-495]]. Both
tasks passed; these two gaps sit next to what they fixed.

**1. Picker triggers read as unusable fields.** `b-date-range-picker` (custom mode), `b-date-picker`,
`b-datetime-picker` and `b-time` render `<input type="text" readonly>`, which opens a popup on
Enter/Space (`b-date-range-picker.ts` `_onInputKeydown`) or on click. NVDA announces **"Stay dates Start
date, edit, read-only"**. The name is correct (TASK-495), but nothing says a calendar exists or how to
open it, so a screen-reader user hears a field they cannot use. There is no `aria-haspopup`, no
`aria-expanded` and no `aria-controls`. Whether focus moves into the panel when it opens (and back when it
closes) is unmeasured; this task measures it.

**2. The × clear button is unnamed.** `b-select`'s searchable combo renders
`<button class="combo-clear">&times;</button>` (in `_renderSearchable` and re-inserted by `_selectValue`)
with no `aria-label`. Chromium computes its name as "×", and NVDA reads "times, button" /
"multiplication sign, button". It is the next Tab stop after the combobox. Check `b-date-range-picker`'s
`.drp-clear` too: it has `aria-label="Clear"` hard-coded, not the `bwc.common.clear` key.

## Acceptance criteria

- [ ] Each picker trigger announces that it opens a popup and whether it is open: ARIA 1.2
      `role="combobox"` + `aria-haspopup="dialog"` + `aria-expanded` + `aria-controls` → the panel, or an
      equivalent the owner accepts after a screen-reader pass. It keeps the TASK-494/495 names.
- [ ] Opening the panel from the keyboard puts focus somewhere operable inside it, and Escape returns
      focus to the trigger. Measured per control, with any that already behave recorded as such.
- [ ] `b-select`'s clear button (both render sites) is named through `bwc.common.clear` ("Clear"), and
      `b-date-range-picker`'s clear uses the key instead of hard-coded English.
- [ ] `a11y-name-check.mjs` (roles, `haspopup`, the clear button's name) and a Playground smoke (focus in
      and out of the panel) cover it, proven able to fail.

## Out of scope

- The calendar grid's own keyboard model (arrow keys across days) — record a verdict when measuring focus.

## Human test plan

- [ ] Screen reader: Tab to a date range, a date and a time picker. Each announces a popup and its state;
      Enter opens it, you can pick a date without the mouse, and Escape returns you to the field. The ×
      after a filled Menu group reads "Clear, button".
