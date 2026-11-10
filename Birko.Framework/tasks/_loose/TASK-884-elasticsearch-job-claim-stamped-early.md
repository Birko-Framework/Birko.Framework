---
id: TASK-884
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-879]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Elasticsearch job queue stamps a claim with the dequeue's start time

## Context

Found by TASK-879 (2026-11-09), by reading the code; not reproduced. `ElasticSearchJobQueue.cs` (around line 97) sets
`m.LastAttemptAt = now`, where `now` is taken at the start of the dequeue, before the candidate search. TASK-879 proved
the same shape wrong on RavenDB: a slow candidate query made a fresh claim look stale, so it was reclaimed at once, and
every claim's age is overstated by the dequeue's latency. Its test passes on Elasticsearch only because the search is
fast.

## Acceptance criteria

- [x] Reproduce (rule 54) with a slow candidate query (TASK-879's store-double approach), offline and live.
      → Reproduced 2026-11-10 with a slow-search store double (claim timeout 300 ms + 150 ms), offline
        (`InMemoryElasticStore`) and live (the real `AsyncElasticSearchStore`, ES 7.17.25): `LastAttemptAt` was 188 ms and
        476 ms older than the claim's return, and the next `DequeueAsync` handed out the job claimed a moment before.
      → Other backends, probed: **SQL** (`SqlJobQueue`, SQLite) and **MongoDB** (`MongoDBJobQueue`) have the same defect,
        reproduced (TASK-900). Cosmos DB and RavenDB (TASK-879) stamp inside the conditional write; Redis stamps in one
        atomic Lua call; InMemory, JSON and XML stamp when they write.
- [x] The claim is stamped when it is written, as RavenDB and Cosmos DB now do.
      → `ElasticSearchJobQueue.DequeueAsync` sets `LastAttemptAt = DateTime.UtcNow` inside the `UpdateIfAsync` lambda,
        after the realtime GET and before the `_seq_no` / `_primary_term`-conditioned write. `staleBefore` keeps the
        dequeue's start, which can only delay a reclaim.
- [x] Tests, red before the fix.
      → `TASK_884_a_claim_behind_a_slow_candidate_search_is_not_reclaimed_at_once`, offline and live: red on HEAD's
        production, green after; the full BackgroundJobs.ElasticSearch suite live 10 runs in a row, 24/24 each.

## Out of scope

- RavenDB (TASK-879, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
