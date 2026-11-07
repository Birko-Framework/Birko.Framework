---
id: TASK-844
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A Cosmos DB view property named like a reserved word fails every query

## Context

Found by TASK-840 (2026-11-07) on the emulator. `CosmosViewStore` writes the projection alias unquoted
(`c["Sub"] AS Inner`), so a view property named like a Cosmos SQL reserved word (`Inner`, `Value`, …) makes every query
on that view fail with a 400 syntax error. The failure is loud.

## Acceptance criteria

- [ ] Reproduced on the emulator with a view property `Inner` and one named `Value`.
- [ ] The alias is written so any property name works, for example quoted the way Cosmos SQL allows, or a generated
      alias mapped back to the property. Reads and counts on such a view equal the C# oracle.
- [ ] A regression test red before the fix.

## Out of scope

- Projections of nested members (TASK-843).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
