---
id: TASK-996
parent: EPIC-019
feature: null
status: done
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

- [x] When the database create fails because another client is creating it, the store goes on once the database is
      listed and answers, in both the sync and async stores. A failure that lasts past the 30 s deadline still throws.
- [x] A regression test, live under `BIRKO_RAVEN_URL`: N stores initialise one new database at the same instant, and
      all succeed (`Task996ConcurrentDatabaseCreateLiveTests`, 8 stores, 5 rounds, sync and async). Before the fix it
      failed with `DatabaseDisabledException`.
- [ ] The TASK-995 contract on RavenDB passes the two-writers test. This is checked at TASK-995, which owns the RavenDB
      event store.

## Out of scope

- The same race on Elasticsearch indexes ([[TASK-997]]).

## Done 2026-11-27

**Measured while fixing.** The server does not answer the race in one way:
- A create refused because another one is running comes back as "already exists" or, more often, as
  `DatabaseDisabledException` ("unloaded and locked"). Both are plain server exceptions, with only message text to tell
  them apart.
- With eight clients at once, every create can be refused, so a store that only waited for another client's create
  waited out its deadline.
- A database that is listed can still be loading: the first query fails with the same `DatabaseDisabledException`. This
  showed only while the rest of the live suite was loading the server (465 of 467 passed, then 464 of 467).

**Fix.** The sync and async stores now share `RavenDatabaseBootstrap.EnsureReady`. It retries a refused create, briefly
spaced, until the database is listed. Then it retries a statistics request while that request answers
`DatabaseDisabledException`. Both have a deadline of `ConcurrentCreateWait` (30 s), after which the last failure is thrown.
A database that exists and is loaded costs one listing and one statistics request at init. `System.Random` is
qualified, because `Birko.Random` shadows it in consumers that import it (this broke Symbio's build until fixed).

**Verified.** The regression test passes 3 runs in a row. The full live suite passes, 467 of 467, including under the
load that showed the loading window. Symbio.Birko builds.

## Human test plan

N/A — fully covered by automated tests (live suite).
