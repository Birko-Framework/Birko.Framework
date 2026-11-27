---
id: TASK-997
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

# Elasticsearch: two clients creating one new index at once, the second fails

## Context

Found at [[TASK-995]]. The event store contract ran live through `IStoreFactory` with the default event store. On
Elasticsearch, the test "two writers changing one aggregate at once" failed in store init:

```
Status code 400 from: PUT /t995c52ea909cea6_eventrecord. ServerError: Type: resource_already_exists_exception
```

Each store's init checks whether its index exists and then creates it: `AsyncElasticSearchStore.cs` around lines 96–100,
`ElasticSearchStore.cs` around lines 93–95, and possibly `IndexManagement/IndexManager.cs`. Two clients that start
together both see "missing", and the second create fails with `resource_already_exists_exception`. The index it wanted
is there, but the call throws.

Any two app instances starting at once on a fresh index hit this. It is separate from Elasticsearch's near-real-time
reads, which TASK-995's Elasticsearch override handles.

## Acceptance criteria

- [x] `resource_already_exists_exception` on index create is treated as "found", in the sync and async stores. Every
      other create failure still throws. `IndexManager.CreateIndex` is left as it is: it is an explicit create, and it
      refuses an existing index on purpose (rule 49).
- [x] A regression test, live under `BIRKO_ES_URL`: N stores initialise one new index at the same instant, and all
      succeed (`Task997ConcurrentIndexCreateLiveTests`, 8 stores, 5 rounds, sync and async). Before the fix it failed with
      `resource_already_exists_exception`.
- [ ] The TASK-995 contract on Elasticsearch passes the two-writers test. This is checked at TASK-995, which owns the
      Elasticsearch event store.

## Out of scope

- Read-your-writes for the event store (TASK-995's Elasticsearch override).
- The same race on RavenDB ([[TASK-996]]).

## Done 2026-11-27

**Measured while fixing.** Treating "already exists" as found was not enough. A store that lost the race returned at about
190 ms, before the creator's create had returned (about 280 ms), and its first count then failed with "all shards failed"
(`no_shard_available_action_exception`). On a single node the index is listed, and its health reads yellow, a moment
before its shard can serve a search. A wait for yellow health alone still failed 7 of 8 stores per round.

**Fix.** Both stores' init now ends with `ElasticSearchStoreHelper.WaitForIndexReady`, whether the index was found,
created, or lost to another client's create. The wait asks for yellow health, then retries a count while
`no_shard_available_action_exception` is its only error. Both have a deadline of `ConcurrentCreateWait` (30 s), after
which it throws. A ready index answers on the first count. `IndexAlreadyExists` reads the server's error type.

**Verified.** The regression test passes 3 runs in a row; a probe reached 40 of 40 stores ready. The full live suite
passes: 740 of 740.

## Human test plan

N/A — fully covered by automated tests (live suite).
