---
id: TASK-903
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
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

# Cosmos DB capability leftovers: nested-predicate aggregates, view decimal AVG, one translator

## Context

Recorded by TASK-899 (2026-11-10); none returns wrong rows:
- Aggregates whose filter holds a nested predicate are refused (TASK-876): the provider renders it as a scalar-subquery
  `JOIN`, which could be accepted.
- An exact view decimal AVG would need the response read raw instead of through the serializer (refused today).
- One translator for store and view: 54 of 165 measured shapes would be answered instead of refused; the cost is listed
  in TASK-899.
- `IS_DEFINED` / `IS_NULL` query cost on a real Cosmos account (the emulator gives no metrics).

**Deferred by the user (2026-11-10)** until a consumer runs on Cosmos DB in production (none does: Symbio has it
selectable but not configured). Nothing here returns wrong rows; each item is a refusal that could be lifted, or a cost
that needs a real account. Also carries TASK-905's deferred items: DateTimeOffset support (a UTC-normalising key) and
Cosmos computed properties for index-served DateTime sorts and filters (needs a real account; the emulator does not
evaluate them).

## Acceptance criteria

- [ ] Decide each on cost; implement or record.
- [ ] Tests, red before any change.

## Out of scope

- The items TASK-899 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
