---
id: TASK-936
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A clearable `b-select` cannot be cleared

Reported by the Symbio session (the user confirmed it): in Symbio admin, Obchod → Produkty, the store-visibility filter
(`filter-isPublished`, options true/false, `clearable`) cannot go back to "all" once "Zobrazené" is picked. The same
happens with Symbio's `filter-missingLanguage`. Both use the stock `filterDefs` `select` with `clearable: true`.

## Cause

`BaseCrudPage` renders a filter select as `<b-select ... clearable>`, and `b-select` ignored `clearable`. A
non-searchable `b-select` renders a native `<select>` whose placeholder option is always `disabled`, so once a value is
picked no option leads back to "no value". The searchable (combobox) mode was not affected: it shows a × by default
(`no-clear` turns it off). Symbio's guess that `_applyFilterDefs()` re-running `setOptions` resets the select is not
the cause: the value survives re-renders (TASK-924).

## Change (Birko.Web v0.4.17)

- `b-select` observes `clearable`. In native mode it keeps the empty option, labelled by `placeholder`, pickable.
  Picking it removes the `value` attribute and emits `change` with `''`, which `BaseCrudPage` turns into "no filter".
  `clearable` without a `placeholder` renders a blank empty option.
- Without `clearable` the placeholder stays a disabled prompt, as a required field wants.
- The placeholder text is escaped. It was inserted into the markup raw.
- Docs: API.md (`clearable` row), the Components CLAUDE.md table, the `FilterDef.clearable` comment.
- Playground: the gallery's Select card gets a `clearable` toggle. Its options were `<option>` children, which
  `b-select` never reads, so the card had shown an empty list; it now calls `setOptions`.

The user chose this look (an enabled placeholder option) over a × button beside the select (2026-11-16).

## Evidence

Playground backport-smoke:
- On Birko.Web main, 2 of the 3 TASK-936 checks fail: the placeholder option is disabled, and picking `''` leaves the
  value attribute set.
- With the change it passes 361/361, 3 of 3 runs. verify.mjs reports 0 failing checks.

keyboard-check passes 225/225, a11y-name-check and a11y-description-check pass, and Components `tsc` is clean.

## Acceptance criteria

- [x] A clearable native `b-select` keeps its placeholder option pickable; picking it clears the value and emits
  `change` with `''`.
- [x] Without `clearable` the placeholder stays disabled.
- [x] The user confirmed the look in the Playground gallery (2026-11-17).

## Human test plan

1. In Symbio, open Obchod → Produkty and set the store-visibility filter to "Zobrazené". The list narrows.
2. Open the filter again and pick "Libovolný stav v obchodě". The list shows all products and the request carries no
   `isPublished` parameter.
3. Repeat with the missing-language filter.
