---
id: TASK-663
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
blocks: [TASK-175]
findings: [SH-M014, SH-M015, SH-M016, SH-M017, SH-M018]
pr: null
github-issue: null
jira-key: null
---

# Redis job queue: atomic transitions, a status-aware dequeue, and priority that dominates

## Context

Spawned by [[TASK-158]], which confirmed all five. They share one root cause: each transition is several round trips
outside a script, while only the dequeue is a Lua script (CR-M025).

- **SH-M014:** the dequeue script picks by score only, so a descriptor enqueued as Cancelled / Completed / Dead is run.
- **SH-M016:** enqueue is three round trips (hash, queue sorted set, status set); a failure between them strands a job.
- **SH-M017:** Complete / Fail / Cancel move status-set membership in four commands; a job ends up listed under two
  statuses, and Fail rewrites the whole hash from a snapshot (SH-M013's shape on Redis).
- **SH-M018:** Cancel checks the status, then flips it in a separate command (TOCTOU), so it can cancel a running job.
- **SH-M015 (decided by the user, 2026-10-18: align with the other backends):** priority only spans about 0.1 ms of
  score, so a mixed-priority queue drains FIFO. Every other backend drains highest priority first.

## Acceptance criteria

- [ ] Enqueue, Complete, Fail and Cancel are each one Lua script; Cancel and the dequeue check Status in the script
- [ ] Eligibility (due) and ordering (priority, then age) are decoupled, so priority strictly dominates among due jobs
- [ ] Live tests (`BIRKO_REDIS_HOST`): a Cancelled descriptor is never dequeued; a higher-priority job enqueued later
      runs first; cancel of a running job is false; status sets stay disjoint under concurrent transitions

## Human test plan

N/A — library behaviour, covered by unit and live tests.
