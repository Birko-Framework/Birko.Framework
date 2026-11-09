---
id: TASK-879
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

# `BackgroundJobs.RavenDB`: the TASK-664 reclaim test fails on HEAD

## Context

Found by TASK-868 (2026-11-09): `JobQueueLiveTests.TASK_664_an_abandoned_job_runs_again_after_the_claim_timeout`
("a job claimed just now is not reclaimed") failed 3 of 3 live runs, also with TASK-868's change reverted.

## Acceptance criteria

- [x] Reproduce; decide whether the queue or the test is wrong (rule 54), and record it.
      → Reproduced 2026-11-09 live (RavenDB 6.0): alone the test passed 5/5; in the full suite it failed 2 of 3. **The
        queue was wrong.** `RavenDBJobQueue.DequeueAsync` took `now` before the candidate query and stamped that `now`
        as the claim's `LastAttemptAt`. Under suite load the first dequeue took 519, 333 and 418 ms (measured), longer
        than the test's 300 ms claim timeout, so the claim was already stale when written and the next dequeue reclaimed
        it. Falsified: time zones (all UTC), stale index reads (the claim is checked on the document loaded by id), a
        wrong test. Cosmos DB stamps inside its compare-and-set and MongoDB reads `now` per attempt; Elasticsearch has
        RavenDB's shape (TASK-884). In production every claim's age was overstated by the dequeue's own latency.
- [x] Fix the one that is wrong, with the test green and stable over repeated runs.
      → The claim's update sets `LastAttemptAt = DateTime.UtcNow` inside `UpdateIfAsync`, so a retried save takes the
        time again (the Cosmos DB shape). `staleBefore` still comes from the dequeue's start, which can only delay a
        reclaim.
      → `TASK_879_a_claim_behind_a_slow_candidate_query_is_not_reclaimed_at_once` (offline, a store double whose query
        takes the timeout + 150 ms): red 3/3 before, with the live failure's message. Green: offline 25/25; full live
        suite 10 consecutive runs, 25/25 each, the TASK-664 test included.
      → **Found, filed:** TASK-884 (Elasticsearch job queue stamps the claim with the dequeue's start time).

## Out of scope

- Null orderings (TASK-868, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
