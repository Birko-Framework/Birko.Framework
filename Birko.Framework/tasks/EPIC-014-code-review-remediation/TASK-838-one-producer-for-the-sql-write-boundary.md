---
id: TASK-838
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-833]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# One producer for "join the ambient SQL transaction, or own one"

## Context

TASK-833 added `AbstractConnector.RunAtomically` / `RunAtomicallyAsync` for chunked writes. The method joins the
ambient transaction, or opens one, publishes it as ambient, commits and runs the after-commit work, or rolls back and
discards it. The SQL bulk stores' `Atomically` / `AtomicallyAsync` (`DataBaseBulkStore`, `AsyncDataBaseBulkStore`)
repeat the same shape. That makes two producers of one boundary rule, free to drift apart, as rules 16 and 53
describe.

## Acceptance criteria

- [ ] The bulk stores' `Atomically(Async)` delegate to `RunAtomically(Async)`, or the reverse, leaving one place that
      opens, publishes, commits and rolls back.
- [ ] The existing boundary tests (TASK-242 and TASK-833's rollback-at-chunk-k tests) stay green; every SQL importer
      suite stays green.

## Out of scope

- Retrying chunked writes: their own transaction retries the BEGIN only, as the bulk store does (rule 66).

## Human test plan

N/A — fully covered by automated tests: a refactor with existing boundary tests.

## Implementation plan
