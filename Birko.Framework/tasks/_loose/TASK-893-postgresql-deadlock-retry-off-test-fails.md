---
id: TASK-893
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Reproduce (rule 54) alone and in the suite; decide whether the retry path or the test is wrong, with evidence.
      → Reproduced 2026-11-10 live (postgres:16): in the class run, retry-off after the retry test failed 8 of 8; alone it
        passed 3 of 3. **The test was wrong, not the retry path.** PostgreSQL checks a waiting session for deadlock once,
        when `deadlock_timeout` (1 s) expires. The test closed the cycle from the other session only after polling with an
        unpooled connection whose open took ~2 s on Windows `localhost` (measured 2.03–2.05 s); with a warm pool the
        framework was already waiting at ~300 ms, its single check found no cycle, and the other session was the 40P01
        victim 10 s later, so the framework committed and nothing was thrown. With the arrangement fixed, retry-off
        surfaces 40P01 with `IsNotAppliedException` true and nothing applied; the default policy retries once and applies
        once.
- [x] Fix the wrong one; the test passes reliably over repeated runs.
      → Test only: the framework's statement closes the cycle itself (`UPDATE row 1; DO $$ loop $$; UPDATE row 2`, the
        loop waiting until a session is blocked by it via `pg_locks` / `pg_blocking_pids`, exiting once row 2 is free,
        bounded at 15 s with its own error); the other session requests row 1 with `deadlock_timeout` 10 s once the
        framework is in its loop. No timing left. Assertions unchanged.
      → 20 of 20 class runs green in a row, retry-off alone 5 of 5, full PostgreSQL suite 1767/1768 (the one failure is
        `string.Equals(Text, Opt, Ordinal)` expected to refuse — TASK-895's in-progress two-column work). SQL Server's
        deadlock tests are not exposed (its lock monitor re-checks). Note: `localhost` costs ~2 s per unpooled open on
        Windows; `127.0.0.1` avoids it.

## Out of scope

- None recorded.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
