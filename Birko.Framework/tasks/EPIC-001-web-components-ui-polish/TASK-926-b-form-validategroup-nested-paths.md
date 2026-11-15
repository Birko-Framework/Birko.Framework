---
id: TASK-926
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: null
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

- [ ] `validateGroup('adv')` validates `adv`'s values and reports `adv.extra`, as `validate()` does.
- [ ] Clears and returns errors only under that prefix.
- [ ] Regression check in the Playground (red before); extend `form-conditional-smoke` with the `validateGroup()`
      half of TASK-925 (a conditionally hidden field is skipped).
- [ ] Check consumers calling `validateGroup` (Symbio, Shell) for code that relied on the `root.`-prefixed keys.

## Human test plan

N/A — fully covered by automated tests.
