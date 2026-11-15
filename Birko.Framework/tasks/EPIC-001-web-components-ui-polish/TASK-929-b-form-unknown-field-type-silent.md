---
id: TASK-929
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: null
created: 2026-11-15
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-form renders an unknown field `type` as a text box, silently

Reported by the Symbio session (TASK-1090), 2026-11-15: a schema with `type: 'segmented'` (no such `FieldType`; the
right one was `option-group`) rendered a plain text input with no warning. Symbio's Playwright spec caught it, at the
cost of a gate-3 round. `_fieldTag`'s `default:` branch is `b-input` for text, password, email, number, and anything
else.

TypeScript rejects the literal in a typed schema, so this reaches consumers through schemas built loosely (spread,
`as`, JSON config files, which `setSchema` accepts).

This is rulebook rule 41 ("a mapper that cannot express something refuses; it never drops it quietly") on a smaller
scale. Decide the answer before building:

- a `console.warn` naming the field and the type (cheap, keeps rendering), or
- refusing in `setSchema` (throw), which matches rule 41 but can break a consumer's form at runtime.

## Acceptance criteria

- [ ] The behaviour for an unknown type decided (warn or refuse) and recorded here.
- [ ] Known types listed in one place, which `_fieldTag` and the check share.
- [ ] Playground check: an unknown type is reported, by name.

## Human test plan

N/A — fully covered by automated tests.
