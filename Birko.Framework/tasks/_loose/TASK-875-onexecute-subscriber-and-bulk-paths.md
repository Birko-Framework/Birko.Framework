---
id: TASK-875
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-871]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `OnExecute`: a throwing subscriber fails the operation; bulk paths may not raise it

## Context

Found by TASK-871 (2026-11-09), by reading the code; not reproduced.
- **A throwing subscriber:** the providers' built-in `OnException` handler (`EnsureSchemaAndReport`) passes the
  subscriber's exception back to the caller, so a logging subscriber that throws fails the read or write. Rule 25 says a
  diagnostic channel must survive its own subscriber.
- **Bulk paths:** PostgreSQL binary COPY, MySQL bulk insert and other paths that bypass the command runners may not
  raise `OnExecute` at all.
- **`ExecuteReader` errors** (TASK-880): an error other than a missing table, thrown by `ExecuteReader`, goes straight
  to the caller without `InitException` / `OnException`, while errors from `Read()` pass through them.

## Acceptance criteria

- [ ] Reproduce each (rule 54), per provider, sync and async. Record which operations fail on a throwing subscriber,
      and which statements never reach `OnExecute`.
- [ ] A throwing subscriber no longer changes the operation's outcome (rule 25), and every statement sent reaches
      `OnExecute` through `AnnounceCommand`, or the gap is recorded with its reason.
- [ ] Tests, red before the fix.

## Out of scope

- The text's form (TASK-871, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
