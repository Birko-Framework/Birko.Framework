---
id: TASK-489
parent: EPIC-001
feature: FEATURE-001
status: todo
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

## Acceptance criteria

- [ ] The searchable input exposes the ARIA 1.2 combobox pattern: `role="combobox"`,
      `aria-expanded`, `aria-controls` → the listbox, `aria-autocomplete="list"`.
- [ ] The dropdown is `role="listbox"`, each option `role="option"` with `aria-selected`, and group labels
      are exposed as groups (or are presentational), never as options.
- [ ] ArrowDown/ArrowUp open the list and move a highlighted option (`.active`), tracked through
      `aria-activedescendant`. Enter selects the highlighted option. Home/End are optional.
- [ ] The `creatable` create row takes part in the same navigation as a final option.
- [ ] Enter with no highlighted option and an empty query still falls through to `b-form`'s
      submit-on-Enter. That is the behaviour [[TASK-488]] pinned.
- [ ] Playground smoke asserts the roles, the arrow-key movement, and that `aria-activedescendant` follows
      it. It is proven able to fail.
- [ ] Nothing changes in native (non-searchable) mode, which is already a real `<select>`.

## Out of scope

- `b-multi-select`'s keyboard navigation. Record a verdict on it here; if it is warranted, spawn it.
- Restyling the dropdown.

## Human test plan

- [ ] NVDA or Narrator on a searchable `b-select` in the Playground: the field is announced as a combobox,
      the match count or the highlighted option is read while arrowing, and selecting announces the
      value.
