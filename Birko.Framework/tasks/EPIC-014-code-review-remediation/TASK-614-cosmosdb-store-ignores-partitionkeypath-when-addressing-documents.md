---
id: TASK-614
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
related: [TASK-610, TASK-163]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The CosmosDB store creates its container on `PartitionKeyPath` but always addresses documents by Guid

## Context

Found 2026-10-14 by the cross-provider audit spawned from TASK-610, and confirmed in source. Not measured.

`Settings.PartitionKeyPath` (default `/id`) is a public knob. The container is created with it
(`AsyncCosmosDBStore.cs:563`, `CosmosDBStore.cs:429`), but every point operation passes
`new PartitionKey(<entity Guid>)`: create, read, replace, delete, upsert and the bulk loops
(`AsyncCosmosDBStore.cs:219,234,289,316,364,461,511,540`, `CosmosDBStore.cs:165,182,…`). The value is only correct
when the path is `/id`. With any other path, such as `/tenantId`, Cosmos rejects every write because the partition key
in the request does not match the one in the document, and point reads look in the wrong partition. That makes it a
knob the mechanism cannot deliver (rule 10) and a silent option (rule 55).

Related, but not the same: SH-M286 (`GetId` omits `PartitionKeyPath`; TASK-163 made `StoreLocator` refuse colliding
settings) and SH-M174 (the migrations builder records a partition key and never applies it).

## Acceptance criteria

- [ ] Decide on measurement: either the store derives the partition key value from the configured path (read the
      document property the path names), or it refuses any `PartitionKeyPath` other than `/id` at
      `SetSettings`/init with a message naming the setting. Record the choice and the reason.
- [ ] Live Cosmos test (emulator, `BIRKO_COSMOS_CONNECTION`): a store configured with a non-`/id` path either
      round-trips create/read/update/delete or refuses up front. It never fails per write. Red first.
- [ ] The sync and async stores behave the same.

## Out of scope

- SH-M174 (the migrations `CosmosCollectionBuilder`) and SH-M286 (the cache identity), which are tracked by their
  own findings.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

_Drafted when picked._
