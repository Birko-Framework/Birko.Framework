---
id: TASK-998
parent: EPIC-019
feature: null
status: todo
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

- [ ] A store query refused with `IndexCreationException` while another client creates the same auto index is answered
      once the index exists, within a bounded wait, in the sync and async stores. Any other failure, or one past the
      deadline, still throws.
- [ ] A regression test, live under `BIRKO_RAVEN_URL`: N stores send the same first query to a new database at the same
      instant, repeated over several rounds, and all answer. The test is shown to fail before the fix.
- [ ] The TASK-995 RavenDB contract passes a 15-run loop with no failure.

## Out of scope

- Static (deployed) indexes for the event store. Measure first whether the retry is enough.

## Human test plan

N/A — fully covered by automated tests (live suite).
