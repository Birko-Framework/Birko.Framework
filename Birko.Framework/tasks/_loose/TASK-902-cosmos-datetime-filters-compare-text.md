---
id: TASK-902
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-899]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB `DateTime` filters compare the stored text, not the instant

## Context

Found by TASK-899 (2026-11-10), reproduced on the emulator: with UTC values stored at 10:00:00 and 10:00:00.5,
`x.T > 10:00:00Z` returns nothing where C# returns the 10:00:00.5 document. The stored fraction has variable length
(`.5Z` sorts before `Z`), and values stored with `Z`, an offset or no suffix compare wrongly against each other too.
Affects store and view filters and view sorts by a `DateTime` field. TASK-897's `WallClockKey` already solved ordering
for aggregates.

## Acceptance criteria

- [ ] Reproduce (rule 54) on reads, counts, writes, aggregates' filters and view sorts: fractions, kinds, offsets,
      `DateTime?`, against C#.
- [ ] Every comparison and sort orders as C# does (one producer, e.g. TASK-897's wall-clock key or a normalized stored
      form), or a shape that cannot is refused by name.
- [ ] Tests, red before the fix.

## Out of scope

- The items TASK-899 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
