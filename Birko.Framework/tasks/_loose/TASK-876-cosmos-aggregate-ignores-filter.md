---
id: TASK-876
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
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

# Cosmos DB `Aggregate` ignores the filter

## Context

Found by TASK-868 (2026-11-09), measured live: `CosmosAggregationHelper.BuildAggregateSql` emits no WHERE clause, so
`Aggregate` / `AggregateAsync` aggregate every document whatever `query.Filter` says. `A > 3` aggregated all 6 rows.
Every filter, silently wrong. There is no live Cosmos DB aggregate test.

## Acceptance criteria

- [ ] Reproduce (rule 54) live: plain, negated, `Contains`, null-ordering filters, with each aggregate kind, against C#.
- [ ] The filter is rendered through the store's own translator (the one reads use, `NullOrderingFold` included), or an
      untranslatable filter is refused by name. A filter must never be dropped.
- [ ] Live tests, red before the fix.

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
