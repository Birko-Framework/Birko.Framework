---
id: TASK-878
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-868]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Elasticsearch store aggregation: GROUP BY by property name returns nothing; Count reads 0

## Context

Found by TASK-868 (2026-11-09), measured live but not fully diagnosed: `GroupByFields = ["Bucket"]` (the C# property
name) returned no groups; the camelCase `"bucket"` returned the right groups, but a `Count` metric read 0 with source
`"A"` or `"a"`. There is no live Elasticsearch aggregate test.

## Acceptance criteria

- [ ] Reproduce (rule 54) live, each aggregate kind, property-name and field-name forms, against C#.
- [ ] Names resolve through the store's mapping (one producer), or an unknown name is refused by name; metrics match C#.
- [ ] Live tests, red before the fix.

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
