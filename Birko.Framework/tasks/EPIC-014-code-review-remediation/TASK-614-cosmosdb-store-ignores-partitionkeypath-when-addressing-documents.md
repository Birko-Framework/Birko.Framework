---
id: TASK-614
parent: EPIC-014
feature: null
status: done
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

- [x] Decide on measurement: either the store derives the partition key value from the configured path (read the
      document property the path names), or it refuses any `PartitionKeyPath` other than `/id` at
      `SetSettings`/init with a message naming the setting. Record the choice and the reason.
      → **Refuse.** Deriving the value works for operations that carry the entity, but `Read(guid)` and delete by
      Guid would have no partition to name and would need cross-partition queries: a feature, not a fix. Nothing
      uses another path (Symbio, the main consumer, keeps the default), so refusing breaks no working setup. The docs
      showing `/tenantId` were corrected.
- [x] Live Cosmos test (emulator, `BIRKO_COSMOS_CONNECTION`): a store configured with a non-`/id` path either
      round-trips create/read/update/delete or refuses up front. It never fails per write. Red first.
      → The refusal is offline (`PartitionKeyPathTests`, red first: no exception). Live, a container that already
      exists on `/tenantId` is refused by the SDK at init with both paths named. Measured, so that case needed no code;
      `PartitionKeyPathLiveTests` pins it.
- [x] The sync and async stores behave the same.

## Out of scope

- SH-M174 (the migrations `CosmosCollectionBuilder`) and SH-M286 (the cache identity), which are tracked by their
  own findings.

## Human test plan

N/A — covered by the live provider test.

## Implementation plan

1. `Settings.RequireAddressablePartitionKeyPath()` throws `NotSupportedException` naming the path.
2. Call it wherever settings land: `SetSettings(ISettings)` and the connection-string constructors of both stores.
3. Tests: offline refusals for both stores and constructors, `/id` accepted, and a live test for the existing-container case.

## Progress log

- 2026-10-14 — Cosmos vnext-preview emulator (Gateway). Red: three refusal tests (no exception). The live
  existing-container test showed the SDK already refuses (`ArgumentException`: requested '/id' does not match
  '/tenantId'), so it pins that instead. Green: CosmosDB 98, Views 35, ViewModel 5, Migrations.Cosmos 29, Sync.Cosmos
  14, Workflow.Cosmos 8, BackgroundJobs.Cosmos 9. CS8629 count unchanged (22). Not measured: the original per-write
  error with a fresh `/tenantId` container, because it is now unreachable.
