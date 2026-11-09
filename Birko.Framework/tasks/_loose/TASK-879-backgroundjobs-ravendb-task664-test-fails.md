---
id: TASK-879
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

# `BackgroundJobs.RavenDB`: the TASK-664 reclaim test fails on HEAD

## Context

Found by TASK-868 (2026-11-09): `JobQueueLiveTests.TASK_664_an_abandoned_job_runs_again_after_the_claim_timeout`
("a job claimed just now is not reclaimed") failed 3 of 3 live runs, also with TASK-868's change reverted.

## Acceptance criteria

- [ ] Reproduce; decide whether the queue or the test is wrong (rule 54), and record it.
- [ ] Fix the one that is wrong, with the test green and stable over repeated runs.

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
