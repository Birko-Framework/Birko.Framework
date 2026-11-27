---
id: TASK-997
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

- [ ] `resource_already_exists_exception` on index create is treated as "found", in the sync and async stores and in
      `IndexManager` if it has the same check-then-create. Every other create failure still throws.
- [ ] A regression test, live under `BIRKO_ES_URL`: N stores initialise one new index at the same instant, and all
      succeed. The test is shown to fail before the fix.
- [ ] The TASK-995 contract on Elasticsearch passes the two-writers test.

## Out of scope

- Read-your-writes for the event store (TASK-995's Elasticsearch override).
- The same race on RavenDB ([[TASK-996]]).

## Human test plan

N/A — fully covered by automated tests (live suite).
