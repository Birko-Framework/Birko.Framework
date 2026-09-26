---
id: TASK-496
parent: EPIC-001
feature: FEATURE-001
status: done
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

### Measured when starting (2026-09-26)

- **Opening does not move focus.** `_openPanel` (all four) shows the panel and leaves focus on the input.
  The day buttons are reachable only by tabbing onward, and nothing says they are there.
- **Escape works only on the input.** With focus in the panel, Escape does nothing.
- **Picking loses focus.** `_selectDate` / `_close` hide the panel while focus is on a button inside it, so
  focus falls to `<body>`.
- **Every action in a calendar loses focus.** `_refreshPanel` rebuilds the panel with `innerHTML` after
  month navigation, the month picker and the first date of a range, destroying the focused button. So a
  keyboard user gets exactly one action. This blocks the "pick a date without the mouse" criterion, so it
  is fixed here.
- Clear buttons: `b-date-picker` / `b-datetime-picker` `.dp-clear` and `b-time` `.tp-clear` are unnamed
  "×" (two render sites each); `b-date-range-picker` `.drp-clear` is hard-coded `aria-label="Clear"`;
  `b-select` `.combo-clear` is unnamed (two sites).

## Implementation plan

1. One helper, `inputs/picker-popup.ts`, used by all four pickers instead of four hand copies:
   `triggerAria(uid, open)` (`role="combobox" aria-haspopup="dialog" aria-expanded aria-controls`),
   `panelAria(uid, name)` (`id`, `role="dialog"`, `aria-label`), `setExpanded`, `focusIntoPanel`
   (the selected cell, else today, else the first enabled control), and `refreshKeepingFocus(panel, render)`,
   which re-focuses the element with the same `data-*` key after a rebuild.
2. Each picker (custom mode only; native inputs are already right): trigger and panel ARIA in render.
   `_openPanel(…, focusPanel)` moves focus in when opened from the keyboard. `_close()` collapses and, if
   focus was inside the panel, returns it to the trigger. Escape inside the panel closes. `_refreshPanel`
   goes through `refreshKeepingFocus`.
3. Clear buttons, every render site: `aria-label` from the component's clear label (`bwc.common.clear`).
4. `a11y-name-check.mjs`: trigger computed as `combobox` with a popup, and clear buttons named "Clear".
   New `picker-popup-smoke`: per picker, keyboard open → focus in the panel; month navigation keeps focus;
   pick → value set and focus back on the trigger; Escape in the panel → closed and focus on the trigger.
   Prove both fail first.

## Acceptance criteria

- [x] Each picker trigger announces that it opens a popup and whether it is open: ARIA 1.2
      `role="combobox"` + `aria-haspopup="dialog"` + `aria-expanded` + `aria-controls` → the panel, or an
      equivalent the owner accepts after a screen-reader pass. It keeps the TASK-494/495 names.
- [x] Opening the panel from the keyboard puts focus somewhere operable inside it, and Escape returns
      focus to the trigger. Measured per control, with any that already behave recorded as such.
- [x] `b-select`'s clear button (both render sites) is named through `bwc.common.clear` ("Clear"), and
      `b-date-range-picker`'s clear uses the key instead of hard-coded English.
- [x] `a11y-name-check.mjs` (roles, `haspopup`, the clear button's name) and a Playground smoke (focus in
      and out of the panel) cover it, proven able to fail.

## Out of scope

- The calendar grid's own keyboard model (arrow keys across days). **Verdict (2026-09-26):** not needed for
  access. Every day is a real `<button>`, so Tab/Shift+Tab reach any date and Enter picks it, and focus now
  survives each rebuild. Arrow-key grid navigation would be faster, not required, so there is no task.

## Human test plan

- [x] Screen reader: Tab to a date range, a date and a time picker. Each announces a popup and its state;
      Enter opens it, you can pick a date without the mouse, and Escape returns you to the field. The ×
      after a filled Menu group reads "Clear, button".

## Progress log

- 2026-09-26 — New `inputs/picker-popup.ts` (`triggerAria`, `panelAria`, `setExpanded`, `focusIntoPanel`,
  `refreshKeepingFocus`, `returnFocus`, `clearButton`), used by `b-date-picker`, `b-datetime-picker`, `b-time`
  and `b-date-range-picker` (custom mode): combobox trigger + named dialog panel, keyboard open moves focus
  in, rebuilds keep it (by `data-*` key, enabled matches only), Escape in the panel closes it, and close hands
  focus back to the trigger (for the range, the endpoint it was opened from). The nine clear-button render
  sites (including `b-select`'s two) are named via the component's clear label; the range's hard-coded
  "Clear" now uses the key. New locale keys `bwc.{date,datetime,time,daterange}.dialog`. README note.
- 2026-09-26 — `picker-popup-smoke` 55/55 (16/52 on the pre-change components) and `a11y-name-check` 32/32
  (8 failing before: four `textbox` triggers, four "×" buttons). `verify.mjs` 0 failing in three consecutive
  runs, `device-fix-check` 68/68, `a11y-description-check` PASS, `tsc` clean.
- 2026-09-26 — ⚠ **Focus checks were flaky, and it was the harness, not the components.** Every smoke
  suite shares one page; focus is page-global, and while another suite has a modal `<dialog>` open the rest
  of the page is inert, so `focus()` silently fails. It failed on a different picker each run, and every
  case passed in isolation. Fix: `Birko.Web.Playground/src/smoke-focus.ts` `focusable()` waits until the
  element really takes focus (asserted as a precondition), then the keyboard sequence runs with no `await`.
  The same precondition is added to TASK-489's `select-combobox-smoke`, which had the same exposure.
  (The modal-inertness mechanism fits every observation but was not isolated directly.)
- 2026-09-26 — Checked in passing: `b-time`'s `_refreshPanel` re-runs `_wirePanel`, which looked as if it
  would stack click listeners. Measured: each hour-up press adds exactly one hour. Not a defect.
- The screen-reader step is pending → `review`.
- 2026-09-26 — **Screen-reader step FAILED (owner, NVDA).** (1) The pickers are read as "combo edit" with no
  expanded/collapsed. Chrome does hand over `combobox, expanded=false, hasPopup=dialog`, but as
  `editable` and **not settable**, because the input is `readonly`. TASK-489's Menu group is the same
  combobox but settable, and NVDA reads it correctly ("combo box, collapsed"). NVDA evidently treats a
  read-only editable combo as an edit field. (2) The time picker's ▲/▼ read only "button". (3) The calendar's
  ◀/▶ read as "reverse play button" / "play button". Both are unnamed glyph buttons, and the hour/minute boxes
  and day buttons ("10") are unnamed or context-free too. The smoke and name check passed because they asserted the
  trigger's attributes and the × buttons, and never the panel's own controls or the settable state.
  → Back to `in-progress`: drop `readonly` for a typing guard (`beforeinput` + `inputmode="none"`), the one
  configuration measured to work in NVDA; name every glyph button, the time boxes and each day
  ("10 September 2026", `aria-current="date"` on today); extend both checks to cover them.
- 2026-09-26 — Fixed after the failed pass. The triggers drop `readonly` for `TYPING_GUARD_ATTRS` + `guardTyping`
  (`beforeinput` cancelled; IME text that lands is restored), which makes Chrome report `settable: true`, the
  shape NVDA read correctly on Menu group. `navAria` names ◀/▶ ("Previous month" … `bwc.date.*`, which also
  replaces the range picker's hard-coded English), `dayAria` names each day "10 September 2026" (today gets
  `aria-current="date"`), and `b-time`'s spinners and boxes are named ("Increase hours", "Hours" … `bwc.time.*`).
  `a11y-name-check` 43/43 (9 failing on the previous commit: not settable, glyph buttons, bare days, unnamed
  time controls); `picker-popup-smoke` 67/67 (55/67 before: not readonly, typing refused, stray text restored).
  `verify.mjs` 0 failing twice, `device-fix-check` 68/68, `a11y-description-check` PASS. The human test plan is
  unticked again → `review`, pending the owner's second NVDA pass.
- 2026-09-26 — Owner's second NVDA pass: "Previous month" / "Next month" are read correctly; `b-time`'s spinners
  "sound good"; the field says **"collapsed"** (the state the first pass lost). NVDA's word for the role is "combo
  edit", and TASK-489's Menu group, which the owner accepted, says the same, so it is NVDA's term for an
  editable combobox, not a defect. After Enter NVDA reads the focused day button rather than "expanded": focus
  has moved into the panel by design, and Escape returns it with "collapsed". Confirmed by ear right after:
  NVDA reads **"Start date 10 September 2026 button"** (the dialog's name, the day, the role). → `done`.
