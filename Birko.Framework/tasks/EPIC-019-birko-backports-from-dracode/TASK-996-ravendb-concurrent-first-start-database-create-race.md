---
id: TASK-996
parent: EPIC-019
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-27
depends-on: []
blocks: [TASK-995]
related: [TASK-995, TASK-997]
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB: two clients starting on a new database at once, the second fails

## Context

Found at [[TASK-995]]. The event store contract ran live through `IStoreFactory` with the default event store. On RavenDB,
the test "two writers changing one aggregate at once" failed before either writer appended anything:

```
Raven.Client.Exceptions.RavenException : Raven.Client.Exceptions.ConcurrencyException: Database 't995bbc8fcf6ab5e' already exists!
```

Two factories, like two processes, start on a database that does not exist yet. Each store's lazy init checks whether the
database exists and then creates it (`CreateDatabaseOperation`, `AsyncRavenDBStore.cs` around line 748, and the sync
`RavenDBStore`). Both see "missing", both create it, and the second create throws. The client's call fails even though
the database it needed now exists.

This is not specific to event stores: any two instances of an app starting at the same moment on a fresh RavenDB
database hit it. Every other contract test passed on RavenDB, including duplicate refusal and the all-or-nothing batch.

## Acceptance criteria

- [ ] When the database create fails because the database already exists, the store goes on as if it had found it, in
      both the sync and async stores. Every other create failure still throws.
- [ ] A regression test, live under `BIRKO_RAVEN_URL`: N stores initialise one new database at the same instant, and
      all succeed. The test is shown to fail before the fix.
- [ ] The TASK-995 contract on RavenDB passes the two-writers test.

## Out of scope

- The same race on Elasticsearch indexes ([[TASK-997]]).

## Human test plan

N/A — fully covered by automated tests (live suite).
