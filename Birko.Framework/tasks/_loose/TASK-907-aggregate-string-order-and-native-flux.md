---
id: TASK-907
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-906]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates: string MIN/MAX order, enums stored as text, native Flux

## Context

Found by TASK-906 (2026-11-10):
- **String MIN/MAX order (measured):** over {apple, Banana, éclair, zebra}, MongoDB gives Min "Banana", Max "éclair"
  (binary order); the LINQ oracle gives "apple" / "zebra" because `Comparer.Default` is culture-sensitive (sk-SK here).
  SQL collations, ES and Cosmos likely differ too (not measured). **Decide with the user** whether the oracle should
  compare ordinally (consistent with TASK-873's exact string equality) — Cosmos and ES already refuse string MIN/MAX.
- An enum stored with `[BsonRepresentation(String)]` would take its MongoDB Min/Max by name, not value (not measured).
- InfluxDB no longer aggregates server-side (TASK-906 removed the wrong native path); an exact native Flux path could be
  added if read-everything cost matters.

## Acceptance criteria

- [ ] Measure string MIN/MAX per provider; get the user's decision on the oracle's string order and record it.
- [ ] Implement it, or refuse by name where a provider cannot match; enums-as-text measured and fixed or refused.
- [ ] Native Flux aggregation decided on cost.

## Out of scope

- Null handling in aggregates (TASK-886, TASK-906, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
