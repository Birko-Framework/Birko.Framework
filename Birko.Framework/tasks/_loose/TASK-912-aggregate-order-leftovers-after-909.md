---
id: TASK-912
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-909]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates and reads: ordering and collation leftovers after TASK-909

## Context

Found by TASK-909 (2026-11-11), not fixed there:
- **MongoDB on a collection with a default collation:** store reads and counts (`Find`) and MongoDB views still follow
  the collation (measured for aggregates: `$match` matched `Zebra` for `== "zebra"`); only aggregates run with
  `Collation.Simple` now.
- **PostgreSQL NULL position for non-string keys:** an aggregate ORDER BY on an int or DateTime key puts NULL last; the
  in-memory oracle puts it first (NULL-first was added for string keys only).
- **Row order of Mongo/ES/Cosmos/Raven views** ordered by a string group key: not checked.
- **Guid and enums stored as text** as ordering keys may order differently in memory and on the servers (not measured).

## Acceptance criteria

- [ ] Measure each (rule 54).
- [ ] Fix or refuse by name; tests red before.

## Out of scope

- String GROUP BY merging (TASK-911).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
