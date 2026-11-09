---
id: TASK-884
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce (rule 54) with a slow candidate query (TASK-879's store-double approach), offline and live.
- [ ] The claim is stamped when it is written, as RavenDB and Cosmos DB now do.
- [ ] Tests, red before the fix.

## Out of scope

- RavenDB (TASK-879, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
