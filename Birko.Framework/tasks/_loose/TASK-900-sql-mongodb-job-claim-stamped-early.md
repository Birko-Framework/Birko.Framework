---
id: TASK-900
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Reproduce on each (rule 54), offline and live.
      → Reproduced 2026-11-10 with a slow candidate-read store double (300 ms timeout + 150 ms): the claim's
        `LastAttemptAt` came back 500–730 ms older than the claim (SQLite and MongoDB offline; MongoDB, SQLite, PostgreSQL,
        MySQL, SQL Server live), and on every target the next `DequeueAsync` handed out the job claimed a moment before.
- [x] The claim is stamped right before the guarded update, as RavenDB, Cosmos DB and Elasticsearch now do.
      → Both queues' claim update sets `LastAttemptAt = DateTime.UtcNow` (TASK-879 / TASK-884 shape); `staleBefore`
        keeps the dequeue's start; TASK-575's single-reclaim guard is unaffected.
- [x] Tests, red before the fix; the live suites stable over repeated runs.
      → `Task900ClaimStampTests<DB>` (SQLite offline, PostgreSQL/MySQL/SQL Server live, unique queue per test) and
        MongoDB offline + live `TASK_900_…` tests: red on HEAD (SQL 4/4, Mongo 2/2), green after. Live 10 consecutive runs:
        BackgroundJobs.MongoDB 23/23, BackgroundJobs.SQL 65/65 each.
      → Consumers: Symbio's `SqlJobQueue` (PostgreSQL, SQL Server, SQLite) and BardStudio use the default 30-minute claim
        timeout, so the defect only fired on a read slower than that; `LastAttemptAt` is now the true claim time.

## Out of scope

- Elasticsearch and RavenDB (done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
