---
id: TASK-883
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-864]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Live SQL contract suites share fixed table names, so concurrent runs collide

## Context

Found by TASK-864, TASK-866 and TASK-874 (2026-11-09): the shared StoreContract fixtures use fixed table names
(`T869TextRows`, `T860CallRows`, `T866CollapsedRows`…). Two test runs against the same live server at once drop and
refill each other's tables: duplicate keys, "Invalid object name", schema-change errors, the PostgreSQL `pg_type`
unique violation. Test infrastructure only, but it makes parallel and CI runs flaky.

## Acceptance criteria

- [ ] Reproduce with two concurrent runs of one provider suite.
- [ ] Each run uses its own table names (or schema / database), from one producer in the shared fixtures, and drops
      them afterwards.
- [ ] Two concurrent runs pass.

## Out of scope

- Production code.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
