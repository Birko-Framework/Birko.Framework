---
id: TASK-662
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
blocks: [TASK-175]
findings: [SH-M003]
pr: null
github-issue: null
jira-key: null
---

# JobDescriptor.MaxRetries becomes nullable: null = the queue's RetryPolicy, 0 = no retries

## Context

Spawned by [[TASK-158]] (SH-M003, confirmed). Six backends compare `AttemptCount < MaxRetries` with no fallback (SQL,
including its reclaim; MongoDB, Elasticsearch, XML, Cosmos DB, Redis). Three use `MaxRetries > 0 ? MaxRetries :
policy.MaxRetries` (InMemory, JSON, RavenDB). So a descriptor with `MaxRetries = 0` is dead on its first failure on six
backends and follows the policy on three.

**Decided by the user (2026-10-18): make it nullable.** `null` means "use the queue's RetryPolicy", and `0` means "no
retries". A signature change on `JobDescriptor` and on every backend's model / column.

## Acceptance criteria

- [x] `JobDescriptor.MaxRetries` is `int?`; the default value is decided and recorded (keeping 3 changes nothing for
      existing callers; `null` hands every default job to the policy)
- [x] One producer of "how many attempts does this job get" (e.g. on `RetryPolicy`), used by all nine backends, the
      SQL reclaim included
- [x] Every backend model and column accepts null; the SQL column change is reported as drift and its repair stated
- [x] CHANGELOG breaking entry with the old and new signature
- [x] Tests: null follows the policy, 0 dies on the first failure, n gives n attempts, on every backend

## Human test plan

N/A — library behaviour, covered by unit and live tests.

## Outcome (2026-10-18)

- **Default kept at 3.** Existing callers change nothing; `null` is opt-in. `RetryPolicy.MaxRetriesFor(int?)` is the
  one producer; InMemory, JSON, XML, SQL (claim and reclaim), MongoDB, Elasticsearch, RavenDB, Cosmos DB and Redis ask it.
- **Models:** `int?` with no initializer, so a stored null reads back as null (XML `IsNullable = true`; Redis stores
  null as an empty field).
- **SQL column, measured on SQLite:** a table created before has `MaxRetries NOT NULL`, refuses a null budget, and
  `DetectDrift` reports `NullabilityMismatch`. The repair per provider is in the CHANGELOG and `docs/background-jobs.md`.
- **Tests:** a theory (null -> the policy's 2, 0 -> 1, 1 -> 1, 3 -> 3, null stored as null) on all nine backends, Mongo,
  Raven and Redis also live (new `JobQueueLiveTests`). Mutation (InMemory back to `> 0 ? :`): 0 red. The two
  CR-L025 / CR-L029 tests that pinned 0 = the policy now pin null.
- **Found on the way:** the first live MongoDB job-queue run could not claim at all. `MongoPropertyUpdateTranslator`
  built `$set` values with `BsonValue.Create`, which throws for a `Guid` under driver 3.x. Values are now written by the
  member's own serializer. Red on HEAD: a rendering test and a live store test (`Task662GuidSetLiveTests`).
- **Corrected TASK-158:** its file and the SH-M012 verdict said Mongo and Raven passed live; they ran on doubles.
- The Redis live transitions test moved to [[TASK-663]] (atomic Redis transitions are its scope).

**Regression, live:** the 10 BackgroundJobs suites (core 110, Hosting 9, JSON 13, XML 13, SQL 55, Redis 34, Mongo 14,
ES 12, Raven 15, Cosmos 13) and the 6 other importers of `Birko.Data.MongoDB` (Data.MongoDB 150, Migrations.MongoDB 19,
ViewModel 6, Views 16, Sync.MongoDb 6, Workflow.MongoDB 8) green. Sandbox builds.
