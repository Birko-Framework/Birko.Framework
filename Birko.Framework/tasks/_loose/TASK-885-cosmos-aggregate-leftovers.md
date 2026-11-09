---
id: TASK-885
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-876]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB aggregates: nullable SUM/MIN, ignored time bucket, a second translator

## Context

Found by TASK-876 (2026-11-09):
- **SUM and MIN over a nullable field return null** when any value in the group is null, even with no filter
  (measured: `Sum(Score)` null where C# gives 140, `Min(Score)` null where C# gives 10). Silently wrong. MAX was right;
  AVG not measured.
- **`TimeBucketInterval` / `TimeColumn` are ignored** by the store aggregate, with no error (seen in code; rule 41).
- **`CosmosViewStore` aggregates use their own `CosmosFilterTranslator`**, not the LINQ provider the store uses, so the
  two WHERE clauses come from two translators.
- **An empty ungrouped aggregate:** in-memory `LinqAggregate` returns no rows; Cosmos DB and SQL return one row with
  count 0.

## Acceptance criteria

- [ ] Reproduce each (rule 54) live, against C#.
- [ ] Nullable aggregates match C# (skip nulls, as LINQ does) or are refused by name; the time bucket is translated or
      refused; record whether the view translator should become the store's (one producer) and do it or file it; decide
      one answer for the empty ungrouped aggregate across providers.
- [ ] Tests, red before the fix.

## Out of scope

- The filter (TASK-876, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
