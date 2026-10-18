---
id: TASK-663
parent: STORY-053
feature: FEATURE-014
status: done
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

- [x] Enqueue, Complete, Fail and Cancel are each one Lua script; Cancel and the dequeue check Status in the script
- [x] Eligibility (due) and ordering (priority, then age) are decoupled, so priority strictly dominates among due jobs
- [x] Live tests (`BIRKO_REDIS_HOST`): a Cancelled descriptor is never dequeued; a higher-priority job enqueued later
      runs first; cancel of a running job is false; status sets stay disjoint under concurrent transitions

## Human test plan

N/A — library behaviour, covered by unit and live tests.

## Outcome (2026-10-18)

- **Reproduced live first (red on HEAD):** Cancelled / Completed / Dead / Processing descriptors were dequeued (M014,
  4/4); a priority-5 job enqueued 30 s after a priority-0 one ran second, and priority -1 ran before 0 (M015); racing
  Complete and Fail left jobs in two status sets (M017); 4 dequeuers racing 4 cancellers over 60 jobs claimed and
  cancelled the same job, 5 of 5 runs (M018 — the first cold run happened to pass); Complete / Fail of a pending job
  acted on it. M016 (a failure between enqueue's round trips) cannot be provoked live; one script fixes it by construction.
- **Layout:** `{prefix}:queue:{name}` is now the delayed set (due time, `Ticks / 1e4` — the old key and scale, so jobs
  stored before are promoted when due; pinned by a live test) and `{prefix}:ready:{name}` ranks due jobs
  `(999 - priority) * 1e12 + EnqueuedAt ms since 2020`. Priority is clamped to ±999 (it was clamped to 0..999, so a
  negative priority now runs after the default).
- **Scripts:** enqueue, dequeue, Complete, Fail and Cancel are each one Lua script that checks status and writes the
  hash, status sets and queue sets together. Fail decides the retry from what it read and applies it only if the job is
  still that Processing attempt; it writes only `Status`, `LastError` and the time, never the whole hash.
- **Tests:** 10 new live tests (`Task663RedisAtomicityLiveTests` + the transitions test moved from TASK-662); 9 red
  on HEAD; green 5 of 5 runs after. Mutation (no status check in dequeue / enqueue / promote): 4 red. The two offline
  score tests now target the priority-free `GetDueScore`.
- **Spec fixed beyond Redis:** "Completion is unconditional on status" and "a second lock name succeeds without
  locking" contradicted TASK-158 and were rewritten.

**Regression, live:** BackgroundJobs.Redis 44 (its only importer). Sandbox builds.
