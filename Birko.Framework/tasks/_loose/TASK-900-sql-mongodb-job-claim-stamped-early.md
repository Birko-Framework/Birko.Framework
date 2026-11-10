---
id: TASK-900
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-884]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL and MongoDB job queues stamp a claim with the dequeue's start time

## Context

Found by TASK-884 (2026-11-10), reproduced offline with a slow candidate read: `SqlJobQueue` (`now` taken before the
candidate read ~line 99, written `.Set(j => j.LastAttemptAt, now)` ~line 184; SQLite probe) and `MongoDBJobQueue`
(`now` ~line 70, written ~line 140; `InMemoryMongoStore` probe) stamp the claim with the dequeue's start. A slow read
makes a fresh claim look stale, and the next dequeue hands the same job out again. Same defect TASK-879 (RavenDB) and
TASK-884 (Elasticsearch) fixed. SQL is the backend Symbio would use.

## Acceptance criteria

- [ ] Reproduce on each (rule 54), offline and live.
- [ ] The claim is stamped right before the guarded update, as RavenDB, Cosmos DB and Elasticsearch now do.
- [ ] Tests, red before the fix; the live suites stable over repeated runs.

## Out of scope

- Elasticsearch and RavenDB (done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
