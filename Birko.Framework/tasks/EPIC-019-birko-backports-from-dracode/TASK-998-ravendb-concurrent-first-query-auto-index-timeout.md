---
id: TASK-998
parent: EPIC-019
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-27
depends-on: []
blocks: [TASK-995]
related: [TASK-995, TASK-996]
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB: two clients' first identical query on a new database, one fails after 15 s

## Context

Found at [[TASK-995]]. The RavenDB event store contract ran 15 times with full output. Twice, "two writers changing one
aggregate at once" failed on the writers' first `GetVersion`. That is a store query
(`AsyncRavenDBStore.ReadCoreAsync`, filter plus order), and RavenDB answered it with:

```
Raven.Client.Exceptions.Documents.Indexes.IndexCreationException: Failed to create auto index
'Auto/EventRecords/ByAggregateGuidAndVersion', the operation timed out after: 00:00:15.
```

A dynamic query makes RavenDB create an auto index for its shape. When two clients send the first query of that shape
to a new database at once, both ask for the index, and one request timed out waiting for it. Measured 2 of 15 runs. The
index exists afterwards, so the same query would then answer.

This is not specific to event stores. Any two app instances whose first identical query reaches a fresh database
together can hit it. It is the query-side sibling of [[TASK-996]] (database creation).

## Acceptance criteria

- [x] A store query refused with `IndexCreationException` is sent again, up to 3 times, in the sync and async stores.
      Any other failure, or the last attempt's, still throws. Widened during the fix: `DatabaseDisabledException` is
      retried too, for queries and saves (below).
- [x] ~~A dedicated regression test~~: one was written (8 stores, query-then-save, 6 rounds) and passed 36 of 36 before the
      fix, so it proved nothing and was deleted (rule 54). The red is the TASK-995 RavenDB contract itself: 3 failures in
      30 runs before the fix, with load and without.
- [x] The TASK-995 RavenDB contract passes a 15-run loop with no failure: **45 of 45**.

## Out of scope

- Static (deployed) indexes for the event store. Measure first whether the retry is enough.

## Done 2026-11-27

**Measured.**
- The failure is a 15 s auto-index creation timeout (`IndexCreationException`) on a fresh database: 2 of 15 runs under
  load, 1 of 15 without. Load was a hypothesis and was falsified.
- After the query retry, the event store contract passed 45 of 45.
- The full RavenDB suite then failed TASK-996's two tests with `DatabaseDisabledException` ("unloaded and locked") on a
  count *after* start. A create request from another client makes the server unload and lock a database already in use
  (the server stack: `AdminDatabasesHandler.Put` then `UnloadAndLockDatabaseImpl`). TASK-996's start-up check cannot
  prevent a create that arrives after it.

**Fix.** `RavenQueryRetry` (one helper for both stores) wraps every query (20 points) and every `SaveChanges`
(16 points):
- `IndexCreationException` is retried up to 3 times;
- `DatabaseDisabledException` is retried, briefly spaced, until 30 s have passed.

Both refusals mean the server applied nothing, so resending is safe (rule 66). Any other exception passes through
unchanged.

**Verified.**
- The event store contract passes 45 of 45.
- TASK-996's tests pass 3 of 3 alone.
- The full live suite: run 1 466 of 467 (the one failure: TASK-999's broad-match delete, server-side cancellation, unrelated to this fix); run 2 467 of 467.
- Symbio.Birko builds.

## Human test plan

N/A — fully covered by automated tests (live suite).
