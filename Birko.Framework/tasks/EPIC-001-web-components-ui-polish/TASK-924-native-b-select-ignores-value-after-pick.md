---
id: TASK-924
parent: EPIC-001
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-14
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Native b-select ignores the `value` attribute once the user has picked

Found by Symbio (TASK-1089) while testing TASK-923; the user asked for the fix, relayed by the Symbio session,
2026-11-14.

## Context

In native (non-searchable) mode a re-render only toggles each `<option>`'s `selected` attribute. Once the user has
picked from the inner `<select>`, its selectedness is dirty and the browser ignores that attribute, so a later `value`
attribute change or re-render kept showing the old pick. In Symbio the page re-render cleared then re-set the
attribute, and the picker showed "All" over a filtered tree. Setting the `value` property worked (Symbio keeps that
workaround; it is correct either way).

## Acceptance criteria

- [x] After a user pick, a `value` attribute change shows the new option.
- [x] After a user pick, a re-render (`setOptions`) with the attribute unchanged shows the attribute's option.
- [x] Clear then re-set (Symbio's sequence) shows the new value.
- [x] No value: the placeholder when there is one, else the first enabled option, as an untouched select shows.
- [x] Test that fails before the fix. CHANGELOG, v0.4.14.

## Human test plan

- [ ] Symbio pages screen: pick a category in the filter, then cause the page to re-render with a different category
      set by the page (or clear it): the picker shows the page's value, not the earlier pick. (Symbio's own workaround
      masks this; to exercise the fix alone, use the Playground or remove the workaround locally.)

## Built and merged (2026-11-14), not pushed

`_wireNative` (runs after every render) sets `selectedIndex` to the rendered default option, else the first enabled
one. Playground `select-combobox-smoke`: 36/38 against unfixed code (re-render and clear-then-set fail), 38/38 with the
fix; verify.mjs failing checks 0. Components `tsc` (TypeScript 5.6.3) clean.
Birko.Web main 8cf3adf, tagged **v0.4.14** locally; Playground main 3972e00. Not pushed (batch with the next push,
with the tag). Worktrees and branches removed.
