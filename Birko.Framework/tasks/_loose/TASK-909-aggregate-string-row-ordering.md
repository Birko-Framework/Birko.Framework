---
id: TASK-909
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-907]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates: string ordering of result rows; MongoDB default collation

## Context

Found by TASK-907 (2026-11-10):
- `AggregateHelper.ApplyOrderingAndPaging` orders result rows (group keys, string MIN/MAX values) with `Comparer.Default`,
  culture-sensitive; MongoDB, Elasticsearch and Cosmos DB order their rows through it, while SQL `ORDER BY` uses the
  column collation. **Decide with the user** whether `AggregateQuery.StringComparison` also governs row ordering (default
  Ordinal, as MIN/MAX).
- A MongoDB collection created with a default collation would make `$min`/`$max` follow that collation instead of binary
  order (not measured); `Collation.Simple` on the aggregation would fix it but also changes `$match`.

**Decided by the user (2026-11-11):** yes — `AggregateQuery.StringComparison` (default `Ordinal`) also governs the
ordering of result rows by a string group key or a string MIN/MAX, on every provider (SQL `ORDER BY` through the ordinal
key); a provider that cannot match refuses by name, as for MIN/MAX.

## Acceptance criteria

- [ ] Measure both (rule 54); the user's decision on row ordering recorded.
- [ ] Implement or refuse by name; tests red before.

## Out of scope

- String MIN/MAX order (TASK-907, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
