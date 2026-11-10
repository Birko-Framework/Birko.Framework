---
id: TASK-905
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-902]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB DateTime: leftovers after TASK-902

## Context

Found by TASK-902 (2026-11-10):
- Two wall-clock key formulas: TASK-897's `CosmosAggregationHelper.WallClockKey` (SQL text, with suffix, for aggregates)
  and TASK-902's filter key (expression tree, no suffix). They agree on ordering but should have one producer.
- DateTimeOffset comparisons, lists and sorts are refused; supporting them needs a UTC-normalising key, and Cosmos
  `DateTimeAdd` is not reachable from LINQ.
- `TimeSpan` members were not examined; their text likely has the same variable-length problem.
- An index-served sort and filter would need Cosmos computed properties, which the emulator does not evaluate; needs a
  real account.

## Acceptance criteria

- [ ] Measure each (rule 54).
- [ ] Unify the key producer; fix or refuse TimeSpan; decide DateTimeOffset and computed properties on cost.
- [ ] Tests, red before each fix.

## Out of scope

- DateTime filters and sorts (TASK-902, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
