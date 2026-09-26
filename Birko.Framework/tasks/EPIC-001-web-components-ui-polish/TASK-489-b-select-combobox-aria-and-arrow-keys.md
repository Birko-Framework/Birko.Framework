---
id: TASK-489
parent: EPIC-001
feature: FEATURE-001
status: done
priority: P2
assignee: ai
created: 2026-09-25
depends-on: [TASK-488]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-select`'s searchable combo has no combobox semantics and no arrow-key navigation

## Context

Found while fixing [[TASK-488]]. In searchable (and now `creatable`) mode, `b-select` renders
(`Birko.Web.Components/src/inputs/b-select.ts`, `_renderSearchable`):

- a plain `<input class="combo-input" type="text">`, with no `role="combobox"`, no `aria-expanded`, no
  `aria-controls` and no `aria-activedescendant`;
- a `.dropdown` popover of `<div class="option" data-value>` rows, with no `role="listbox"` /
  `role="option"` and no `aria-selected`.

The only keys handled are Escape, Enter (under `allow-free-text` / `creatable`) and Backspace. **There is no
ArrowUp/ArrowDown.** A keyboard-only user can type to filter but cannot choose one of the filtered
options. Enter picks an option only when the query exactly names it, which [[TASK-488]] added for
`creatable` alone. The stylesheet already has an unused `.option.active` rule, so a highlighted option was
planned at some point.

A screen reader hears a text field. It is not told that a list exists, how many options match, or which
one is highlighted. [[TASK-488]] added a polite live region for the create row only. That covers one row,
not the pattern.

`b-multi-select` is partly further along: its container has `aria-haspopup` / `aria-expanded` /
`aria-controls`, and its dropdown has `role="group"`. It has no arrow-key navigation either. Keep this
task to `b-select`, and note the multi-select gap in the verdict.

## Implementation plan

1. Input: `role="combobox"`, `aria-autocomplete="list"`, `aria-controls` → `${uid}-listbox`,
   `aria-expanded` kept in sync on every open and close path (`_openDropdown`, the input-opens path,
   `_closeDropdown`, `_selectValue`).
2. Dropdown: `role="listbox"`. Rows: `role="option"`, a stable `id`, `aria-selected`. The create row is an
   option. "No matches" is an `aria-disabled` option, never navigable. Grouped options are wrapped in
   `role="group"` labelled by their (`aria-hidden`) group header.
3. Keys: ArrowDown/ArrowUp open the list if closed, then move `.active` and `aria-activedescendant`
   (starting from the selected option, clamped at the ends, scrolled into view). Enter with an active row
   selects it, or creates it for the create row. With no active row, the existing Enter behaviour is kept
   (create / free text / fall through to `b-form` submit). Filtering resets the highlight.
4. **Focus stays in the field.** The Enter and Escape paths called `input.blur()`, which drops keyboard
   focus onto `<body>`: after picking a value, a keyboard or screen-reader user was nowhere. That is this
   task's subject (keyboard use), so it is fixed here.
5. Playground `select-combobox-smoke` (roles, IDREFs, keys, focus) plus a role case in
   `a11y-name-check.mjs` (Chromium computes `combobox`). Prove both can fail.

## Acceptance criteria

- [x] The searchable input exposes the ARIA 1.2 combobox pattern: `role="combobox"`,
      `aria-expanded`, `aria-controls` → the listbox, `aria-autocomplete="list"`.
- [x] The dropdown is `role="listbox"`, each option `role="option"` with `aria-selected`, and group labels
      are exposed as groups (or are presentational), never as options.
- [x] ArrowDown/ArrowUp open the list and move a highlighted option (`.active`), tracked through
      `aria-activedescendant`. Enter selects the highlighted option. Home/End are optional.
- [x] The `creatable` create row takes part in the same navigation as a final option.
- [x] Enter with no highlighted option and an empty query still falls through to `b-form`'s
      submit-on-Enter. That is the behaviour [[TASK-488]] pinned.
- [x] Playground smoke asserts the roles, the arrow-key movement, and that `aria-activedescendant` follows
      it. It is proven able to fail.
- [x] Nothing changes in native (non-searchable) mode, which is already a real `<select>`.

## Out of scope

- `b-multi-select`'s keyboard navigation. **Verdict (2026-09-26):** operable. Its rows are real checkboxes in
  DOM order right after the container, so Tab reaches them and Space toggles them. Arrow keys would be nicer
  but are not needed for access, so there is no task. Its `aria-haspopup="true"` announces a *menu*, which the
  popup is not → an acceptance line on [[TASK-490]], which edits that control anyway.
- Restyling the dropdown.

## Human test plan

- [x] NVDA or Narrator on a searchable `b-select` in the Playground: the field is announced as a combobox,
      the match count or the highlighted option is read while arrowing, and selecting announces the
      value.

## Progress log

- 2026-09-26 — `b-select` searchable mode: `role="combobox"` + `aria-autocomplete` / `aria-expanded` /
  `aria-controls` / `aria-activedescendant`; the dropdown is a `listbox` of `option`s (with ids and
  `aria-selected`), groups are `role="group"` labelled by an `aria-hidden` header, "No matches" is an
  `aria-disabled` option, and the create row is an option. ArrowDown/ArrowUp open the list and move the
  highlight (starting at the selected option, clamped); Enter activates it. The `blur()` calls on Enter and
  Escape are removed, so focus stays in the field.
- 2026-09-26 — `Birko.Web.Playground` `select-combobox-smoke` 31/31 (7/31 against the pre-change `b-select`),
  plus a role case in `a11y-name-check.mjs` (Chromium computes `combobox "Picker combobox"`; before, a
  `textbox`). `verify.mjs` 0 failing, `device-fix-check` 68/68, `a11y-description-check` PASS, `tsc` clean.
  The screen-reader step is pending → `review`.
- 2026-09-26 — The owner ran the screen-reader step and it passed: "Menu group, combo box, collapsed, required, header"; ArrowDown
  expands and reads the options from the selected one; Enter collapses with focus kept; the create row is reachable;
  Escape keeps focus. → `done`. Found in the same pass: the × clear button has no name → [[TASK-496]].
