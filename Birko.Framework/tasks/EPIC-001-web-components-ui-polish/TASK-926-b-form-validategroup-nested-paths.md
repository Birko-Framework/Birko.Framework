---
id: TASK-926
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-15
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-form `validateGroup()` on a nested group validates empty data under the root's name

Found while building TASK-925 (2026-11-15). Predates it.

## Context

`validateGroup(name)` finds the group with `_walkGroups`, whose prefix includes the **root group's name**
(`_walkGroups` builds `path = prefix ? … : group.name` from the root down). So for a schema `{ name: 'root', children:
[{ name: 'adv', children: [...] }] }`, `validateGroup('adv')` gets prefix `'root'`:

- `groupData = _getNestedValue(data, 'root')` is `undefined`, so the group is validated against `{}`: a filled
  `required` field is reported missing.
- Errors are keyed `root.adv.extra`, a path no `[data-field]` has, so they surface as form-level errors, not on the
  field. Measured in the Playground: `validateGroup('adv')` → errors `root.adv.extra`.

Every other path in b-form (`_walkFields`, `_wireFieldEvents`, `validate()`) leaves the root's name out.

## Acceptance criteria

- [x] `validateGroup('adv')` validates `adv`'s values and reports `adv.extra`, as `validate()` does.
- [x] Clears and returns errors only under that prefix.
- [x] Regression check in the Playground (red before); extend `form-conditional-smoke` with the `validateGroup()`
      half of TASK-925 (a conditionally hidden field is skipped).
- [x] Check consumers calling `validateGroup` (Symbio, Shell) for code that relied on the `root.`-prefixed keys.

## Human test plan

N/A — fully covered by automated tests.

## Fixed (2026-11-15), at the user's word ("fix TASK-926")

`validateGroup` finds the group with its own walk, giving each group the field-path form `validate()` uses (no root
name); it validates the group's values, clears and reports only that group's errors and its subgroups' group errors,
and validates the root group as `validate()` does. Measured before: `validateGroup('adv')` reported a filled required
field missing as `root.adv.extra`; `validateGroup('root')` reported `root.label, root.pageGuid, root.url` (the last
conditionally hidden). Consumers: no caller in Birko.Web.Shell, Symbio, Symbio.Modules or the Playground.
Playground `form-conditional-smoke` 18/25 against unfixed code, 25/25 fixed; verify.mjs failing checks 0. Components
`tsc` clean. Birko.Web main 73dc5ba, tagged **v0.4.16** locally; Playground main 98b2b51. Not pushed.
