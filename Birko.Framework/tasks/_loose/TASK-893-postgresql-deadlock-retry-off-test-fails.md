---
id: TASK-893
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# PostgreSQL `DeadlockRetryLiveTests.With_retry_off` fails on HEAD

## Context

Every live PostgreSQL run since at least TASK-859 (2026-11-07) failed
`DeadlockRetryLiveTests.With_retry_off_the_deadlock_reaches_the_caller_and_nothing_is_applied` ("expected a
PostgresException, none was thrown"); it has been treated as known in every task since, and TASK-887 measured it
failing 3 of 3 alone, also with its change reverted. It passed once (TASK-869's run). No task owned it. The test belongs
to TASK-305's retry work.

## Acceptance criteria

- [ ] Reproduce (rule 54) alone and in the suite; decide whether the retry path or the test is wrong, with evidence.
- [ ] Fix the wrong one; the test passes reliably over repeated runs.

## Out of scope

- None recorded.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
