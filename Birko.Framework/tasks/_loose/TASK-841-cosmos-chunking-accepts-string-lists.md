---
id: TASK-841
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-839]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB chunking splits string lists too, since Cosmos compares strings exactly

## Context

`CosmosInListChunking` (TASK-834, reused by TASK-839) refuses a string list past the query-size limit. That rule
comes from TASK-833's SQL decision: under a case- or accent-insensitive collation, two distinct C# strings can be one
database value, and a row could match two chunks.
- Cosmos DB has no collation. `IN` and `ARRAY_CONTAINS` compare strings exactly, and TASK-839 measured that `"N2"`
  does not match `n2`.
- Distinct C# strings are therefore distinct Cosmos DB values, and string chunks are disjoint.
- The refusal blocks lists that would work, in the base store and in views alike.

## Acceptance criteria

- [ ] Confirmed on the live emulator first: a string `IN` / `ARRAY_CONTAINS` matches only the exact string, for case,
      a trailing space and accents. If any of these matches a different string, stop and record it; the refusal then
      stays.
- [ ] `CosmosInListChunking` splits string lists. The SQL planner (`InListChunkPlanner`) keeps its refusal; the rule is
      per provider, stated in each.
- [ ] Live tests: a string list past a lowered limit equals the C# oracle, for read and count, in the base store and
      in a view.

## Out of scope

- The SQL providers' string refusal (TASK-833, the user's decision).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
