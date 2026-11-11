---
id: TASK-914
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-908]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB: a timeout looks like the caller's cancellation and is never retried

## Context

Found by TASK-908 (2026-11-11), measured chain `TaskCanceledException` → `IOException` → `SocketException 995`:
- A client timeout surfaces as an unwrapped `OperationCanceledException` (the store's
  `catch ... when (ex is not OperationCanceledException)` lets it through), so a caller cannot tell a timeout from its
  own cancellation (rule 9: the exception type is a contract).
- `Settings.IsTransientException` looks for `TaskCanceledException` with an inner `TimeoutException`, which the real
  chain never has, so a timeout is never retried even with a `RetryPolicy` (check rule 66: a read may retry, a write
  only when nothing was applied).
- `Settings.LoadFrom` does not copy `RetryPolicy`.

## Acceptance criteria

- [ ] Reproduce each (rule 54).
- [ ] A timeout is distinguishable from caller cancellation; transient classification matches the real chain; `LoadFrom`
      copies `RetryPolicy`. Tests red before.

## Out of scope

- The timeout setting (TASK-908, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
