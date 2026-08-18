---
area: data-sync
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.Aggregates/Core/AggregateDefinition.cs
  - ../Birko.Data.Aggregates/Core/ExpressionHelper.cs
  - ../Birko.Data.Aggregates/Core/IAggregateDefinition.cs
  - ../Birko.Data.Aggregates/Core/RelationshipBuilder.cs
  - ../Birko.Data.Aggregates/Core/RelationshipDescriptor.cs
  - ../Birko.Data.Aggregates/Core/RelationshipType.cs
  - ../Birko.Data.Aggregates/Extensions/SyncPipelineExtensions.cs
  - ../Birko.Data.Aggregates/Mapping/AggregateMapper.cs
  - ../Birko.Data.Aggregates/Mapping/FlattenResult.cs
  - ../Birko.Data.Aggregates/Mapping/IAggregateMapper.cs
  - ../Birko.Data.Aggregates/Mapping/IRelatedDataProvider.cs
  - ../Birko.Data.Aggregates/Mapping/SyncOperation.cs
  - ../Birko.Data.Aggregates/Mapping/SyncOperationType.cs
  - ../Birko.Data.Sync.CosmosDB/Models/CosmosSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.CosmosDB/Stores/AsyncCosmosSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.CosmosDB/Stores/CosmosSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.ElasticSearch/Models/ElasticSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.ElasticSearch/Stores/AsyncElasticSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.Json/Models/JsonSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.Json/Stores/AsyncJsonSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.MongoDb/Models/MongoSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.MongoDb/Stores/AsyncMongoSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.RavenDB/Models/RavenSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.RavenDB/Stores/AsyncRavenSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.RavenDB/Stores/RavenSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.Sql/Models/SqlSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.Sql/Stores/AsyncSqlSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.Sql/Stores/SqlSyncKnowledgeStore.cs
  - ../Birko.Data.Sync.Xml/Models/XmlSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.Xml/Stores/AsyncXmlSyncKnowledgeStore.cs
  - ../Birko.Data.Sync/AsyncSyncProvider.cs
  - ../Birko.Data.Sync/Internal/SyncProviderBase.cs
  - ../Birko.Data.Sync/Models/ConflictInfo.cs
  - ../Birko.Data.Sync/Models/ConflictResolution.cs
  - ../Birko.Data.Sync/Models/ConflictResolutionPolicy.cs
  - ../Birko.Data.Sync/Models/ISyncKnowledgeItem.cs
  - ../Birko.Data.Sync/Models/SyncAction.cs
  - ../Birko.Data.Sync/Models/SyncBatchResult.cs
  - ../Birko.Data.Sync/Models/SyncDirection.cs
  - ../Birko.Data.Sync/Models/SyncError.cs
  - ../Birko.Data.Sync/Models/SyncFilterOptions.cs
  - ../Birko.Data.Sync/Models/SyncItemPreview.cs
  - ../Birko.Data.Sync/Models/SyncOptions.cs
  - ../Birko.Data.Sync/Models/SyncPreview.cs
  - ../Birko.Data.Sync/Models/SyncProgress.cs
  - ../Birko.Data.Sync/Models/SyncResult.cs
  - ../Birko.Data.Sync/Stores/IAsyncSyncKnowledgeItemStore.cs
  - ../Birko.Data.Sync/Stores/ISyncKnowledgeItemStore.cs
  - ../Birko.Data.Sync/Stores/ISyncKnowledgeStore.cs
  - ../Birko.Data.Sync/SyncProvider.cs
  - ../Birko.Data.Sync/SyncQueue.cs
source-commits:   # sibling HEADs when this spec was last written (2026-07-30 16:07:38,
                  # commit acbbe9d). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Data.Aggregates: 8988d3b
  ../Birko.Data.Sync: 981530f
  ../Birko.Data.Sync.CosmosDB: 34cf848
  ../Birko.Data.Sync.ElasticSearch: ecdb2b7
  ../Birko.Data.Sync.Json: 655e991
  ../Birko.Data.Sync.MongoDb: 8e6bc94
  ../Birko.Data.Sync.RavenDB: fa93f5f
  ../Birko.Data.Sync.Sql: 60d30a6
  ../Birko.Data.Sync.Xml: a56a3ff
shaped-by: []
shaped-by-derived: true
shaped-by-unresolved: 80
---

# Bidirectional data sync, conflict resolution and aggregate mapping

## Purpose

This capability moves entities between two stores of the same shape — conventionally a "local" store and a
"remote" store — and remembers what it has already reconciled so that later runs can tell a genuinely new
item from an item that was deleted on one side. `SyncProvider` (synchronous) and `AsyncSyncProvider`
(asynchronous) are the entry points: given a local store, a remote store and a *sync-knowledge* store, they
compare the two sides entity-by-entity (keyed by `Guid`), decide a `SyncAction` per entity, apply it in
batches, resolve conflicts according to a policy, and persist a knowledge row per entity so the next run has
history. A `Preview`/`PreviewAsync` pair produces the same decisions without writing anything.

Sync knowledge is a per-backend concern: `Birko.Data.Sync.Sql`, `.Json`, `.Xml`, `.MongoDb`,
`.ElasticSearch`, `.RavenDB` and `.CosmosDB` each ship a knowledge model plus a store, and they do **not**
all expose the same contract — only some implement the provider-facing
`ISyncKnowledgeItemStore<T>` / `IAsyncSyncKnowledgeItemStore<T>` interfaces, and the RavenDB and CosmosDB
stores expose a different, tenant-parameterised surface instead. `SyncQueue` serialises sync runs.

`Birko.Data.Aggregates` is the companion piece for syncing *shapes* rather than rows: an
`AggregateDefinition<T>` declares which related entities belong to a root, `AggregateMapper<T>` flattens a
relational root plus its children into one nested document (for upload to a document store) and expands a
nested document back into per-child insert/delete operations (for writing to a relational store).

## Requirements

### Requirement: Guid-keyed change detection

The system SHALL key both sides of a sync by the entity's `Guid` property, discovered by reflection once per
provider instance, and SHALL reject a side that contains an entity with no Guid or two entities sharing a
Guid.

`SyncProviderBase`'s constructor resolves `typeof(T).GetProperty("Guid")` and throws
`InvalidOperationException` when the type has no such property. `BuildEntityDictionary` then keys each side,
mapping a non-`Guid` or null property value to `Guid.Empty` via `GetGuid`.

#### Scenario: Entity type without a Guid property

- **Given** a model type `T` that declares no `Guid` property
- **When** a `SyncProvider<TStore, T, TKnowledge>` is constructed
- **Then** an `InvalidOperationException` is thrown with the message `Type <T> must have a Guid property`

#### Scenario: Local entity with an unassigned Guid

- **Given** the local store returns an entity whose `Guid` is null
- **When** `Sync` runs
- **Then** `BuildEntityDictionary` throws `InvalidOperationException` reading `Cannot sync a local entity with an empty Guid; all syncable entities must have a Guid assigned.`
- **And** `Sync`'s outer catch converts it into a single `SyncError` with message `Sync failed`, sets `Success = false` and returns

#### Scenario: Duplicate Guid on the remote side

- **Given** the remote store returns two entities with the same `Guid` value `G`
- **When** `Sync` runs
- **Then** `InvalidOperationException` is thrown reading `Duplicate remote entity Guid 'G' encountered during sync.`

### Requirement: Initial sync is forced to Download and never overwrites existing local rows

The system SHALL treat a scope whose knowledge store reports no last-sync time as an initial sync, SHALL
overwrite `options.Direction` with `SyncDirection.Download`, and SHALL emit `SyncAction.Create` only for
entities that exist remotely and not locally — every other combination is `SyncAction.Skip`.

`Sync`/`SyncAsync` set `isInitialSync = !lastSyncTime.HasValue`, assign `result.IsInitialSync`, then mutate
`options.Direction = SyncDirection.Download`. `DetermineSyncAction`'s initial-sync branch returns
`Create` for remote-only, and `Skip` for both other cases (including "already exists locally").

#### Scenario: First run downloads remote-only entities

- **Given** the knowledge store returns null from `GetLastSyncTime("Products")`
- **And** the remote store holds entity `G` and the local store does not
- **When** `Sync` runs with `Direction = Bidirectional`
- **Then** `result.IsInitialSync` is true, the entity is created in the local store, and `result.Created` is 1

#### Scenario: First run leaves a diverging local row untouched

- **Given** it is an initial sync and entity `G` exists in both stores with different `UpdatedAt` values
- **When** `Sync` runs
- **Then** the action is `Skip`, no store write occurs, and `result.Skipped` is incremented

#### Scenario: Requested direction is reported, not the direction used

- **Given** a caller passes `SyncOptions { Direction = Bidirectional }` on an initial sync
- **When** `Sync` returns
- **Then** `result.Direction` is `Bidirectional` because it was captured before the override
- **And** the caller's own `SyncOptions` instance now has `Direction == Download`, because the override mutates the passed object

### Requirement: Download-only action selection

The system SHALL, when `Direction` is `Download`, create locally what exists only remotely, update locally
whenever the entity exists on both sides, delete locally only when knowledge records the entity as remotely
deleted, and skip otherwise.

`DetermineSyncAction`'s Download branch returns `Create`; `(Update, "remote")`;
`(Delete, DeleteOn = "local")` when `knowledgeItem?.IsRemoteDeleted == true`; else `Skip`.

#### Scenario: Existing-on-both entity is rewritten every run

- **Given** `Direction = Download` and entity `G` exists in both stores with identical contents
- **When** `Sync` runs
- **Then** the action is `Update` with winner `"remote"` and `_localStore.Update(remoteItem, null)` is called
- **And** `result.Updated` is incremented — no comparison against `knowledgeItem.RemoteVersion` is performed

#### Scenario: Local-only entity with no knowledge is skipped in Download

- **Given** `Direction = Download`, entity `G` exists only locally, and no knowledge row exists for `G`
- **When** `Sync` runs
- **Then** the action is `Skip` and `result.Skipped` is incremented

#### Scenario: Local-only entity known to be remotely deleted is deleted locally

- **Given** `Direction = Download`, entity `G` exists only locally, and its knowledge row has `IsRemoteDeleted = true`
- **When** `Sync` runs
- **Then** `_localStore.Delete(localItem)` is called and `result.Deleted` is incremented

### Requirement: Upload-only action selection

The system SHALL, when `Direction` is `Upload`, mirror the Download rules with the sides exchanged: create
remotely what exists only locally, update remotely when both sides hold the entity, delete remotely only
when knowledge records `IsLocalDeleted`, and skip otherwise.

#### Scenario: Local-only entity is created remotely

- **Given** `Direction = Upload` and entity `G` exists only in the local store
- **When** `Sync` runs
- **Then** `_remoteStore.Create(localItem, null)` is called and `result.Created` is incremented

#### Scenario: Remote-only entity known to be locally deleted is deleted remotely

- **Given** `Direction = Upload`, entity `G` exists only remotely, and its knowledge row has `IsLocalDeleted = true`
- **When** `Sync` runs
- **Then** `_remoteStore.Delete(remoteItem)` is called and `result.Deleted` is incremented

### Requirement: Bidirectional both-sides-present never reports a conflict

The system SHALL, when `Direction` is `Bidirectional` and the entity exists on both sides, always return
`SyncAction.Update` with the winner chosen by `GetWinner(local, remote, options.ConflictPolicy)`, and SHALL
NOT emit `SyncAction.Conflict` for this case, SHALL NOT invoke `OnConflict`, and SHALL NOT invoke
`CustomConflictResolver`.

`GetWinner` maps `LocalWins → "local"`, `RemoteWins → "remote"`, `NewestWins → GetNewest(...)`, and every
other value — including `ConflictResolutionPolicy.Custom` — to the default arm `_ => "local"`.

#### Scenario: Custom policy silently degrades to LocalWins

- **Given** `Direction = Bidirectional`, `ConflictPolicy = Custom`, a non-null `CustomConflictResolver`
- **And** entity `G` exists on both sides
- **When** `Sync` runs
- **Then** the winner is `"local"`, `_remoteStore.Update(localItem, null)` is called
- **And** `CustomConflictResolver` is never invoked and `result.Conflicts` stays 0

#### Scenario: NewestWins picks the later UpdatedAt

- **Given** `Direction = Bidirectional`, `ConflictPolicy = NewestWins`, local `UpdatedAt` is later than remote's
- **When** `Sync` runs
- **Then** the winner is `"local"` and the remote store is updated from the local item

### Requirement: Bidirectional one-sided presence branches on the knowledge deletion flags

The system SHALL, when `Direction` is `Bidirectional` and the entity exists on exactly one side, return
`SyncAction.Create` when no knowledge row marks the *other* side as deleted; and when the other side is
marked deleted, SHALL resolve by policy — `RemoteWins`/`LocalWins` producing a `Delete` or a `Create`, and
any other policy producing `SyncAction.Conflict` carrying a `ConflictInfo` with only the surviving item
populated.

#### Scenario: Local-only, remote recorded as deleted, RemoteWins

- **Given** `Direction = Bidirectional`, `ConflictPolicy = RemoteWins`, entity `G` exists only locally with knowledge `IsRemoteDeleted = true`
- **When** `Sync` runs
- **Then** the action is `Delete` with `DeleteOn = "local"` and the local row is deleted

#### Scenario: Local-only, remote recorded as deleted, NewestWins

- **Given** the same state but `ConflictPolicy = NewestWins`
- **When** `Sync` runs
- **Then** the action is `Conflict` with `ConflictInfo { LocalItem = localItem, RemoteItem = null, Reason = "Modified locally but deleted remotely" }`
- **And** `result.Conflicts` is incremented

#### Scenario: Remote-only, local recorded as deleted, LocalWins

- **Given** `Direction = Bidirectional`, `ConflictPolicy = LocalWins`, entity `G` exists only remotely with knowledge `IsLocalDeleted = true`
- **When** `Sync` runs
- **Then** the action is `Delete` with `DeleteOn = "remote"`

### Requirement: Create is applied only under Download or Upload direction

The system SHALL apply a `SyncAction.Create` decision only when `options.Direction` is exactly `Download`
(creating locally from the remote item) or exactly `Upload` (creating remotely from the local item). When
`Direction` is `Bidirectional`, the `Create` case matches neither branch and no store write occurs, yet the
item is still counted as processed and a knowledge row is still emitted for it.

`ProcessBatch`/`ProcessBatchAsync`'s `case SyncAction.Create:` tests
`options.Direction == SyncDirection.Download` then `options.Direction == SyncDirection.Upload`; there is no
`else` and no `Bidirectional` handling.

#### Scenario: Bidirectional new local entity is never uploaded

- **Given** `Direction = Bidirectional` and a brand-new entity `G` exists only in the local store with no knowledge row
- **When** `Sync` runs
- **Then** `DetermineSyncAction` returns `Create`
- **And** neither `_localStore` nor `_remoteStore` is written to
- **And** `result.TotalProcessed` counts the item, `result.Created` stays 0, and a knowledge row for `G` is persisted with `LocalVersion` set and `RemoteVersion = null` (hence `IsRemoteDeleted = true`)

#### Scenario: The second bidirectional run reinterprets that knowledge row as a remote deletion

- **Given** the state left by the previous scenario (knowledge for `G` has `IsRemoteDeleted = true`, `G` still exists only locally)
- **When** `Sync` runs again with `Direction = Bidirectional` and `ConflictPolicy = RemoteWins`
- **Then** the action is `Delete` with `DeleteOn = "local"` and the never-uploaded local entity is deleted

### Requirement: Conflict resolution is applied only when both items are materialised

The system SHALL apply `ConflictResolution.UseLocal` only when `localItem` is non-null **and** `remoteItem`
is non-null, and `ConflictResolution.UseRemote` only when `remoteItem` is non-null **and** `localItem` is
non-null; SHALL count `ConflictResolution.Skip` as a skipped item; and SHALL take no action at all for
`ConflictResolution.Merge`.

`ApplyConflictResolution` guards `UseLocal` with `when localItem != null` and then an inner
`if (remoteItem != null && CanSaveToRemote(...))`; `UseRemote` guards symmetrically. `Merge` has no `case`.

#### Scenario: The only conflicts the provider produces resolve to no store write

- **Given** a `Conflict` raised by the local-only / `IsRemoteDeleted` path, so `remoteItem` is null
- **And** `ConflictPolicy = NewestWins`, so `GetNewestConflictResolution` sees `RemoteItem == null` and returns `UseLocal`
- **When** `ApplyConflictResolution` runs
- **Then** the `UseLocal` arm is entered but its inner `remoteItem != null` guard fails
- **And** no store write occurs, `progress.UpdatedItems` is unchanged, and only `progress.Conflicts` records the event

#### Scenario: Merge resolution is inert

- **Given** `ConflictPolicy = Custom` with a resolver returning `ConflictResolution.Merge`
- **When** a `Conflict` action is applied
- **Then** no store write occurs and neither `UpdatedItems` nor `SkippedItems` is incremented

#### Scenario: Skip resolution counts as a skip

- **Given** a custom resolver returning `ConflictResolution.Skip`
- **When** a `Conflict` action is applied
- **Then** `progress.SkippedItems` is incremented and no store write occurs

### Requirement: Conflict resolution is chosen by policy with a custom hook

The system SHALL invoke `options.OnConflict` for every `ConflictInfo` reaching `ResolveConflict`, SHALL use
`CustomConflictResolver` only when `ConflictPolicy == Custom` and the resolver is non-null, SHALL map
`LocalWins → UseLocal` and `RemoteWins → UseRemote`, SHALL compare `UpdatedAt` for `NewestWins`, and SHALL
fall back to `UseLocal` for any unmatched policy.

#### Scenario: Custom policy with no resolver falls back to UseLocal

- **Given** `ConflictPolicy = Custom` and `CustomConflictResolver = null`
- **When** `ResolveConflict` runs on a `ConflictInfo`
- **Then** `OnConflict` is invoked and the returned resolution is `ConflictResolution.UseLocal`

#### Scenario: NewestWins with a missing item picks the survivor

- **Given** a `ConflictInfo` whose `LocalItem` is null
- **When** `GetNewestConflictResolution` runs
- **Then** it returns `ConflictResolution.UseRemote`

### Requirement: Timestamp comparison for NewestWins

The system SHALL read the entity's `UpdatedAt` property by reflection, accepting both `DateTime` and
`DateTime?` declarations, SHALL treat "no such property", "a property of another type", and "a null value"
alike as no timestamp, and SHALL default to `"local"` whenever either side has no timestamp. When both
timestamps are present and **equal**, the system SHALL select `"remote"`.

`GetUpdatedAt` matches `prop.PropertyType == typeof(DateTime)` or
`Nullable.GetUnderlyingType(prop.PropertyType) == typeof(DateTime)`. `GetNewest` returns
`localUpdatedAt.Value > remoteUpdatedAt.Value ? "local" : "remote"`.

#### Scenario: Equal timestamps hand the win to remote

- **Given** `ConflictPolicy = NewestWins`, `Direction = Bidirectional`, both sides present with byte-identical `UpdatedAt`
- **When** `GetNewest` runs
- **Then** it returns `"remote"` and the local store is overwritten from the remote item

#### Scenario: Model with no UpdatedAt degrades NewestWins to LocalWins

- **Given** a model type with no `UpdatedAt` property and `ConflictPolicy = NewestWins`
- **When** `GetNewest` runs
- **Then** both lookups return null and the result is `"local"`

### Requirement: Version hashes are derived from UpdatedAt with a random fallback

The system SHALL derive an entity's version hash as its `UpdatedAt` rendered with the round-trip `"O"`
format, SHALL return null for a null entity, and SHALL return a **freshly generated random**
`Guid.NewGuid().ToString()` when the entity exists but has no usable `UpdatedAt`.

`GetVersionHash` returns `updatedAt?.ToString("O") ?? Guid.NewGuid().ToString()`.

#### Scenario: Missing timestamp yields a non-reproducible hash

- **Given** an entity with no `UpdatedAt` property
- **When** `GetVersionHash` is called twice on the same instance
- **Then** the two calls return different values
- **And** the persisted knowledge row's `LocalVersion` therefore cannot be compared across runs

#### Scenario: Absent entity yields a null hash which sets the deletion flag

- **Given** entity `G` is absent from the remote side
- **When** a knowledge item is created via `CreateKnowledgeItem(G, localHash, null, options)`
- **Then** `RemoteVersion` is null and `IsRemoteDeleted` is true, because the flag is `string.IsNullOrEmpty(remoteItemHash)`

### Requirement: Save filters gate every write and their block action is honoured per branch

The system SHALL treat a null `CanSaveToLocal`/`CanSaveToRemote` as "permitted", SHALL consult the predicate
otherwise, and on a block SHALL apply `SyncFilterOptions.OnSaveFilterBlock`: `Skip` does nothing,
`LogAsError` invokes `options.OnError` with a `SaveFilter` operation, `ThrowException` throws
`InvalidOperationException`, and `MarkConflict` invokes `options.OnConflict` with a `ConflictInfo` carrying
the block reason.

#### Scenario: A blocked create in Download counts as skipped

- **Given** `filterOptions.CanSaveToLocal` returns false for entity `G` and `OnSaveFilterBlock = Skip`
- **When** the `Create` case runs under `Direction = Download`
- **Then** no local write occurs, `progress.SkippedItems` is incremented, and no callback fires

#### Scenario: A blocked update produces no write and no counter change

- **Given** an `Update` action with winner `"remote"` and `CanSaveToLocal` returning false
- **When** the `Update` case runs
- **Then** the `else if (winner == "local" ...)` branch is also evaluated and fails, so no write occurs
- **And** neither `UpdatedItems` nor `SkippedItems` is incremented

#### Scenario: ThrowException surfaces as a recorded per-item error, not an aborted run

- **Given** `OnSaveFilterBlock = ThrowException` and a blocked `Create` for entity `G` in a batch of many
- **When** `ProcessBatch` runs
- **Then** the `InvalidOperationException` is caught by the per-item handler, a `SyncError` with `ItemGuid = G` and `Operation = "Create"` is added, `progress.Errors` is incremented
- **And** the remaining items in the batch are still processed, and `result.Success` ends up false

#### Scenario: A save-filter throw during conflict resolution escapes result.Errors

- **Given** `OnSaveFilterBlock = ThrowException` and a `Conflict` whose resolution attempts a blocked save
- **When** `ApplyConflictResolution` runs
- **Then** the exception is caught inside `ApplyConflictResolution`, reported only through `options.OnError` with `Operation = "ConflictResolution"`
- **And** it is not added to `result.Errors`, so `result.Success` can still be true

### Requirement: Batching, item capping and cancellation

The system SHALL process the union of local and remote Guids in slices of `options.BatchSize`, SHALL cap the
union to `options.MaxItems` when that value is non-null and `>= 0` and the union is larger, SHALL invoke
`OnBatchStarting` with a 1-based batch number before each slice and `OnBatchCompleted` with a
`SyncBatchResult` after it, and SHALL stop processing further items and further batches once
`options.CancellationToken.IsCancellationRequested`.

#### Scenario: MaxItems truncates the union

- **Given** the union of local and remote Guids has 250 entries and `MaxItems = 100`
- **When** `Sync` runs
- **Then** only the first 100 Guids of `localDict.Keys.Union(remoteDict.Keys)` are processed
- **And** which 100 those are depends on dictionary enumeration order, not on any explicit ordering

#### Scenario: MaxItems of zero processes nothing

- **Given** `MaxItems = 0` and a non-empty union
- **When** `Sync` runs
- **Then** the cap condition `max >= 0 && allGuids.Count > max` holds, the Guid list becomes empty, no batch runs
- **And** `result.Success` is true with all counters at 0, and `SetLastSyncTime` is still stamped

#### Scenario: Cancellation mid-run keeps recorded errors visible

- **Given** two items have already failed and been recorded, and the token is then cancelled
- **When** the batch loop observes the cancellation and breaks
- **Then** in `SyncProvider` the knowledge collected so far is still persisted and `SetLastSyncTime` is still called, because its knowledge writes take no cancellation token
- **And** in `AsyncSyncProvider` the round's knowledge is discarded instead: `CreateAsync`, `UpdateAsync` and `SetLastSyncTimeAsync` are all passed `options.CancellationToken`, so the first of them throws, the outer catch records a `Sync failed` error and nothing is stamped
- **And** `result.Success` is false because it is computed as `result.Errors.Count == 0` alone

### Requirement: Sync knowledge is upserted by splitting inserts from updates

The system SHALL, after all batches, partition the collected knowledge items by whether an existing
knowledge row for the same `EntityGuid` was read at the start of the run: for an existing row with a
non-null `Guid` it SHALL copy that `Guid` onto the new item and route it to a bulk `Update`; otherwise it
SHALL null the item's `Guid` and route it to a bulk `Create`. It SHALL then stamp the scope's last sync
time to `DateTime.UtcNow`.

#### Scenario: First run inserts knowledge rows

- **Given** no knowledge rows exist for scope `"Products"` and three entities are processed
- **When** `Sync` completes
- **Then** all three knowledge items have their `Guid` set to null and are passed to `_knowledgeStore.Create(list, null)`
- **And** `_knowledgeStore.SetLastSyncTime("Products", <utcNow>)` is called

#### Scenario: Second run reuses the existing knowledge row identity

- **Given** a knowledge row for `EntityGuid = G` exists with `Guid = K`
- **When** `Sync` processes `G` again
- **Then** the new knowledge item's `Guid` is set to `K` and it is passed to `_knowledgeStore.Update(list, null)`, producing no duplicate row

#### Scenario: A failure while persisting knowledge discards the whole round's knowledge

- **Given** `_knowledgeStore.Create` throws
- **When** `Sync` runs
- **Then** the outer catch records a `Sync failed` error, `SetLastSyncTime` is never reached, and no knowledge for the round is durable
- **And** the entity writes already applied to the local/remote stores are not rolled back

### Requirement: Preview reports planned actions without writing

The system SHALL expose `Preview`/`PreviewAsync` which reads knowledge and both sides, runs the same
`DetermineSyncAction` decision per Guid, and returns a `SyncPreview` with per-action counters and a
`SyncItemPreview` list — writing nothing to any store, and not stamping the last sync time.

#### Scenario: Preview populates per-item reasons and versions

- **Given** entity `G` exists remotely only, with no knowledge, under `Direction = Download`
- **When** `PreviewAsync` runs
- **Then** `preview.ToCreate` is 1 and the item's `Reason` is `"New item from remote"`, `LocalVersion` is null and `RemoteVersion` is the remote hash

#### Scenario: Preview swallows failures and reports them as a conflict

- **Given** the remote side contains a duplicate Guid, so `BuildEntityDictionary` throws `InvalidOperationException`
- **When** `Preview` runs
- **Then** the general `catch` increments `preview.Conflicts` and returns the partially-filled preview
- **And** no exception reaches the caller, so a structural data error is indistinguishable from a real conflict

#### Scenario: Cancellation is not disguised as a conflict

- **Given** a store read throws `OperationCanceledException`
- **When** `Preview` runs
- **Then** the dedicated `catch (OperationCanceledException)` rethrows and the exception propagates to the caller

### Requirement: Progress callbacks carry a phase snapshot, not accumulated counters

The system SHALL invoke `options.OnProgress` with a newly constructed `SyncProgress` populated only with
`Phase`, `TotalItems` and `ProcessedItems`, leaving `CreatedItems`, `UpdatedItems`, `DeletedItems`,
`SkippedItems`, `Conflicts` and `Errors` at their defaults, and SHALL report the phases
`DetectingChanges` → `ApplyingChanges` → `Completed`, or `Failed` on an unhandled exception.

`ReportProgress` builds `new SyncProgress { Phase, TotalItems, ProcessedItems }`; the provider's internal
`progress` object (which does accumulate) is never passed to the callback.

#### Scenario: Counters are always zero in the callback

- **Given** an `OnProgress` handler and a run that creates 5 items
- **When** `ApplyingChanges` progress is reported
- **Then** the received `SyncProgress.CreatedItems` is 0 while `ProcessedItems` reflects the running count

#### Scenario: Failure phase is reported with zeroed totals

- **Given** an exception escapes the batch loop
- **When** the outer catch runs
- **Then** `ReportProgress(options, SyncPhase.Failed, 0, 0)` is invoked and the returned `SyncResult` has `Success = false` and a populated `Duration`

#### Scenario: Overlapping entities inflate the internal total

- **Given** 10 entities exist on both sides
- **When** `Sync` sets `progress.TotalItems = localDict.Count + remoteDict.Count`
- **Then** that internal total is 20 for 10 distinct Guids; the value surfaced to `OnProgress` during `ApplyingChanges` is `allGuids.Count` (10) instead

### Requirement: Per-item failures are isolated and reported

The system SHALL wrap each item's action application in a try/catch, SHALL record a `SyncError` carrying the
item Guid, the action name as `Operation`, the message `Failed to sync item <guid>`, the exception message
as `Details` and the exception itself, SHALL increment `progress.Errors`, and SHALL continue with the next
item. On a caught failure the item's knowledge row SHALL NOT be emitted.

#### Scenario: A failing store write does not stop the batch

- **Given** `_localStore.Create` throws for entity `G` inside a 50-item batch
- **When** `ProcessBatch` runs
- **Then** one `SyncError` with `ItemGuid = G` is added to the batch result, `result.Processed` is not incremented for `G`, no knowledge item is emitted for `G`, and the other 49 items are attempted

#### Scenario: Batch errors are surfaced twice

- **Given** a batch that produced two errors
- **When** the batch completes
- **Then** those errors appear both in `result.Errors` and in the `SyncBatchResult.Errors` handed to `OnBatchCompleted`

### Requirement: Success is determined by recorded errors alone

The system SHALL set `SyncResult.Success` to `result.Errors.Count == 0` and SHALL NOT let cancellation,
conflicts or skips influence it. It SHALL always populate `StartTime`, `EndTime` and `Duration`, and mirror
`progress` counters into `TotalProcessed`, `Created`, `Updated`, `Deleted`, `Skipped` and `Conflicts`.

#### Scenario: A run with unresolved conflicts is still a success

- **Given** three items resolved as `Conflict` with no write and no error
- **When** `Sync` completes
- **Then** `result.Conflicts` is 3 and `result.Success` is true

### Requirement: Last sync time is derived from knowledge rows, so an empty scope has none

The system SHALL compute a scope's last sync time as the maximum `LastSyncedAt` across that scope's
knowledge rows and return null when the scope has no rows; and SHALL implement "set" by stamping the value
onto every existing row in the scope — which means stamping a scope with no rows persists nothing even
though the value is echoed back to the caller. A null `lastSyncTime` SHALL short-circuit and return null
without touching any row.

This holds identically for `SqlSyncKnowledgeStore`, `AsyncSqlSyncKnowledgeStore`,
`AsyncJsonSyncKnowledgeStore`, `AsyncXmlSyncKnowledgeStore`, `AsyncElasticSyncKnowledgeStore` and
`AsyncMongoSyncKnowledgeStore`.

#### Scenario: Stamping an empty scope leaves it reading as never-synced

- **Given** scope `"Orders"` has zero knowledge rows
- **When** `SetLastSyncTimeAsync("Orders", someTime, ct)` is called and then `GetLastSyncTimeAsync("Orders", ct)`
- **Then** the setter returns `someTime` but the getter returns null, so the next `SyncAsync` treats the scope as an initial sync

#### Scenario: Null time is a no-op

- **Given** scope `"Orders"` has rows
- **When** `SetLastSyncTime("Orders", null)` is called
- **Then** the method returns null immediately and no row is read or written

#### Scenario: The setter rewrites every row in the scope

- **Given** scope `"Products"` has 500 knowledge rows
- **When** `SetLastSyncTimeAsync` stamps the scope
- **Then** all 500 rows get the new `LastSyncedAt`, so a later `GetLastSyncTimeAsync` returns exactly that value

### Requirement: Knowledge-store write strategy differs per backend

The system SHALL, in `AsyncJsonSyncKnowledgeStore` and `AsyncXmlSyncKnowledgeStore`, apply the last-sync
stamp through a **single bulk** `UpdateAsync(items, ...)` call guarded by `items.Count > 0`; and SHALL, in
`SqlSyncKnowledgeStore`, `AsyncSqlSyncKnowledgeStore`, `AsyncElasticSyncKnowledgeStore` and
`AsyncMongoSyncKnowledgeStore`, apply it with **one `Update`/`UpdateAsync` call per row**.

#### Scenario: JSON store rewrites the file once

- **Given** 200 knowledge items in a scope backed by `AsyncJsonSyncKnowledgeStore`
- **When** `SetLastSyncTimeAsync` runs
- **Then** exactly one `UpdateAsync(List<JsonSyncKnowledgeItem>, ...)` call is made

#### Scenario: ElasticSearch store issues one update per document

- **Given** 200 knowledge items in a scope backed by `AsyncElasticSyncKnowledgeStore`
- **When** `SetLastSyncTimeAsync` runs
- **Then** 200 separate `UpdateAsync(item, ...)` calls are made

### Requirement: Only some backends supply a provider-compatible knowledge store

The system SHALL offer `ISyncKnowledgeItemStore<T>` (usable by the synchronous `SyncProvider`) **only** from
`Birko.Data.Sync.Sql`; SHALL offer `IAsyncSyncKnowledgeItemStore<T>` (usable by `AsyncSyncProvider`) from
`Birko.Data.Sync.Sql`, `.Json`, `.Xml`, `.MongoDb` and `.ElasticSearch`; and SHALL expose the RavenDB and
CosmosDB knowledge stores as plain backend stores that implement **neither** interface and provide no
`CreateKnowledgeItem`, so they cannot be passed to either provider.

`SqlSyncKnowledgeStore : DataBaseBulkStore<DB, SqlSyncKnowledgeItem>, ISyncKnowledgeItemStore<...>`;
`AsyncRavenSyncKnowledgeStore : AsyncRavenDBStore<RavenSyncKnowledgeItem>` and
`AsyncCosmosSyncKnowledgeStore : AsyncCosmosDBStore<CosmosSyncKnowledgeItem>` declare no sync interface.

#### Scenario: A synchronous sync against MongoDB is not constructible

- **Given** a caller wanting `SyncProvider<TStore, T, MongoSyncKnowledgeItem>`
- **When** they look for an `ISyncKnowledgeItemStore<MongoSyncKnowledgeItem>` implementation
- **Then** none exists — `Birko.Data.Sync.MongoDb` ships only `AsyncMongoSyncKnowledgeStore`

#### Scenario: The RavenDB knowledge store cannot back AsyncSyncProvider

- **Given** an `AsyncRavenSyncKnowledgeStore`
- **When** it is passed as the `IAsyncSyncKnowledgeItemStore<RavenSyncKnowledgeItem>` argument of `AsyncSyncProvider`
- **Then** it does not compile, because the class implements neither that interface nor `GetLastSyncTimeAsync(string, CancellationToken)` / `CreateKnowledgeItem`

### Requirement: Tenant-scoped knowledge stores filter differently in CosmosDB and RavenDB

The system SHALL scope RavenDB knowledge queries by tenant **only when `tenantId.HasValue`** — a null
`tenantId` matches every tenant's rows in the scope — and SHALL scope CosmosDB queries with an
**unconditional equality** `x.TenantId == tenantId`, so a null `tenantId` is compared as a value and never
widens to the whole scope: no row carrying a tenant id can match, and whether the rows whose own `TenantId`
is null match is left to Cosmos SQL's null-equality rules, the store making no `IS_NULL` allowance for
them. This applies to `GetKnowledge`, `DeleteKnowledge`, `GetLastSyncTime` and
`SetLastSyncTime` in both the sync and async variants of each store.

#### Scenario: Null tenant reads everything in RavenDB

- **Given** scope `"Products"` holds knowledge rows for tenants `A` and `B`
- **When** `GetKnowledgeAsync("Products", null, ct)` is called on `AsyncRavenSyncKnowledgeStore`
- **Then** rows from both tenants are returned

#### Scenario: Null tenant does not widen the CosmosDB query

- **Given** the same distribution in CosmosDB
- **When** `GetKnowledgeAsync("Products", null, ct)` is called on `AsyncCosmosSyncKnowledgeStore`
- **Then** the predicate stays `x.Scope == scope && x.TenantId == tenantId` with `tenantId` null, so neither tenant `A`'s nor tenant `B`'s rows are returned, and the result is at most the rows whose own `TenantId` is null

#### Scenario: Null tenant delete is scope-wide in RavenDB

- **Given** scope `"Products"` holds rows for tenants `A` and `B`
- **When** `DeleteKnowledgeAsync("Products", null, ct)` is called on the RavenDB store
- **Then** every row in the scope is deleted regardless of tenant

#### Scenario: Duplicate EntityGuid within a scope breaks the read

- **Given** two knowledge rows in the same scope and tenant share an `EntityGuid`
- **When** `GetKnowledgeAsync` builds its dictionary via `ToDictionary(x => x.EntityGuid, ...)`
- **Then** `ArgumentException` is thrown

### Requirement: RavenDB knowledge identity is derived deterministically from the natural key

The system SHALL, when converting a foreign `ISyncKnowledgeItem` into a `RavenSyncKnowledgeItem` with a null
`Guid`, derive the `Guid` as an MD5 hash of `"{EntityGuid:N}|{Scope}|{TenantGuid:N}"` so re-syncing the same
natural key upserts one document rather than accumulating duplicates; SHALL copy `TenantGuid` and
`TenantName` across when the source implements `ITenant`; and SHALL return an item that is **already** a
`RavenSyncKnowledgeItem` unchanged, without deriving a Guid and without stamping tenant fields.

#### Scenario: Re-syncing the same natural key reuses one document

- **Given** an `ISyncKnowledgeItem` with `EntityGuid = G`, `Scope = "Products"`, no `Guid`, and no `ITenant` implementation
- **When** `ConvertToRavenItem` is called on two separate runs
- **Then** both produce the same `Guid`, computed as `DeterministicGuid(G, "Products", Guid.Empty)`, so `session.Store` upserts

#### Scenario: An already-Raven item with a null Guid passes through unmodified

- **Given** a `RavenSyncKnowledgeItem` instance whose `Guid` is null
- **When** `ConvertToRavenItem` receives it
- **Then** it is returned as-is with `Guid` still null, and no deterministic Guid or tenant stamp is applied

#### Scenario: Tenant travels on the item, not as a method parameter

- **Given** a source item implementing `ITenant` with `TenantGuid = T`
- **When** it is converted and stored
- **Then** the resulting document carries `TenantGuid = T`, which is what makes the tenant-scoped `Where(x => x.TenantGuid == tenantId.Value)` queries match

### Requirement: CosmosDB knowledge mapping guarantees a Guid and prefers the explicit tenant

The system SHALL map any `ISyncKnowledgeItem` to a `CosmosSyncKnowledgeItem` through
`FromInterface(item, tenantId)`, which SHALL populate a `Guid` with `Guid.NewGuid()` when the source has
none, SHALL let an explicit non-null `tenantId` overwrite the `TenantId` of an item that is already a
`CosmosSyncKnowledgeItem`, and SHALL leave that item's existing `TenantId` intact when `tenantId` is null.
Upsert and replace SHALL use the item's own `Guid` string as both document id and partition key.

#### Scenario: Passthrough item gets a Guid so the partition key dereference is safe

- **Given** an existing `CosmosSyncKnowledgeItem` with `Guid = null`
- **When** `UpdateKnowledgeAsync` maps and upserts it
- **Then** `FromInterface` assigns a fresh `Guid` via `??=` and `new PartitionKey(cosmosItem.Guid!.Value.ToString())` is well-defined

#### Scenario: Null tenant argument does not erase a carried tenant

- **Given** a `CosmosSyncKnowledgeItem` with `TenantId = T`
- **When** `UpdateKnowledgeAsync(items, tenantId: null, ct)` runs
- **Then** the stored document still has `TenantId = T`

#### Scenario: Foreign knowledge items produce a new document identity each round

- **Given** a `SqlSyncKnowledgeItem` (whose `CreateKnowledgeItem` assigns a fresh `Guid.NewGuid()` per call) fed into `UpdateKnowledgeAsync`
- **When** the same entity is synced twice
- **Then** `FromInterface` copies each distinct `Guid` and two separate Cosmos documents are upserted for the same `EntityGuid`

### Requirement: Knowledge models carry the same logical fields with per-backend persistence shape

The system SHALL implement `ISyncKnowledgeItem` on an `AbstractModel` descendant for every backend, with
`EntityGuid`, `Scope`, `LastSyncedAt`, `LocalVersion`, `RemoteVersion`, `IsLocalDeleted`, `IsRemoteDeleted`
and `Metadata`; and SHALL carry backend-specific identity and mapping in addition:
`SqlSyncKnowledgeItem` maps to table `SyncKnowledge` with `Guid` as primary field plus an
`IncrementField Id`; `MongoSyncKnowledgeItem` carries a `[BsonId]` ObjectId string `Id` and is marked
`[BsonIgnoreExtraElements]`; `ElasticSyncKnowledgeItem` carries a deterministic application key `Id`
(`{EntityGuid:N}_{Scope}`) mapped to the non-reserved keyword field `docKey`; `CosmosSyncKnowledgeItem`
and `RavenSyncKnowledgeItem` normalise a null `Scope` assignment to the empty string.

#### Scenario: ElasticSearch key is an application field, not the document id

- **Given** `AsyncElasticSyncKnowledgeStore.CreateKnowledgeItem(G, lh, rh, options)` with `Scope = "Products"`
- **When** the item is built
- **Then** `Id` is `"{G:N}_Products"` and is persisted to the `docKey` field, while the store keys the Elasticsearch document off `AbstractModel.Guid`, which is set to a fresh `Guid.NewGuid()`

#### Scenario: Assigning a null Scope does not produce a null string

- **Given** a `RavenSyncKnowledgeItem`
- **When** `Scope = null` is assigned
- **Then** the getter returns `string.Empty`

#### Scenario: Legacy Mongo documents with a dropped element still load

- **Given** a persisted Mongo document containing a `recordId` element no longer present on the model
- **When** it is deserialized into `MongoSyncKnowledgeItem`
- **Then** the read succeeds, because `[BsonIgnoreExtraElements]` suppresses the driver's default unmapped-element failure

### Requirement: Sync queue serialises runs through one global semaphore

The system SHALL construct `SyncQueue` with a `SemaphoreSlim(maxConcurrentSyncs, maxConcurrentSyncs)` that
is **shared across all scopes**, SHALL record a `QueuedSync` per enqueue in a per-scope-key queue, SHALL
acquire the semaphore before running the operation and SHALL release it in a `finally`. The per-scope queues
are bookkeeping for `GetQueueLength`/`GetAllQueueLengths` only and do not themselves order or exclude work.

#### Scenario: Default queue permits one run at a time overall

- **Given** a `SyncQueue()` with the default `maxConcurrentSyncs = 1`
- **When** operations for two different scopes `"Products"` and `"Orders"` are enqueued concurrently
- **Then** they execute one after the other, because the single semaphore is not per-scope

#### Scenario: A concurrency limit above one lets two runs of the same scope overlap

- **Given** a `SyncQueue(maxConcurrentSyncs: 2)`
- **When** two operations for the same scope `"Products"` are enqueued concurrently
- **Then** both acquire the semaphore and run at the same time — `DequeueNext` only pops a bookkeeping record and does not exclude same-key work

#### Scenario: Cancellation while waiting releases a permit that was never acquired

- **Given** a `SyncQueue(1)` whose permit is held by a running operation
- **When** a second `EnqueueAsync` is cancelled inside `await _semaphore.WaitAsync(cancellationToken)`
- **Then** the `finally` still calls `_semaphore.Release()`, raising the available count above the number of permits actually held
- **And** the cancelled `QueuedSync` record is never dequeued, so `GetQueueLength(scope)` keeps counting it

#### Scenario: Queue keys can incorporate a tenant

- **Given** a subclass overriding the two-argument `GetQueueKey(scope, tenantId)`
- **When** a tenant id `T` is supplied
- **Then** the key is `"{scope}_{T}"`; with a null tenant it is the bare scope

### Requirement: Aggregate definitions are declared fluently, with the lambda shape as the only validation

The system SHALL let a derived `AggregateDefinition<T>` register relationships with
`HasMany(navigation)` (`OneToMany`) and `HasOne(navigation)` (`OneToOne`), SHALL extract the navigation
property name from the lambda — unwrapping a conversion `UnaryExpression` — SHALL throw `ArgumentException`
when the lambda does not resolve to a property, and SHALL expose the registered descriptors as a read-only
list alongside `RootType`. Registration is otherwise unvalidated: nothing requires `Via` or `Through` to
have been called, nothing rejects a null `ForeignKeyProperty`, and nothing rejects the same navigation
property being registered more than once.

#### Scenario: HasMany with Via records a direct foreign key

- **Given** `HasMany(p => p.Tags).Via(t => t.ProductGuid)`
- **When** the definition is inspected
- **Then** a `RelationshipDescriptor` exists with `Type = OneToMany`, `NavigationProperty = "Tags"`, `ChildType = typeof(Tag)` and `ForeignKeyProperty = "ProductGuid"`

#### Scenario: Through rewrites the relationship as many-to-many

- **Given** `HasMany(p => p.Categories).Through<ProductCategory>(j => j.ProductGuid, j => j.CategoryGuid)`
- **When** the definition is inspected
- **Then** the descriptor has `Type = ManyToMany`, `JunctionType = typeof(ProductCategory)`, `JunctionParentFk = "ProductGuid"` and `JunctionChildFk = "CategoryGuid"`

#### Scenario: Through applied to a HasOne overrides the declared cardinality

- **Given** `HasOne(p => p.Primary).Through<Bridge>(j => j.ProductGuid, j => j.OtherGuid)`
- **When** the definition is inspected
- **Then** `Type` is `ManyToMany`, not `OneToOne`, so the mapper subsequently treats the navigation as a collection

#### Scenario: A non-property lambda is rejected

- **Given** `HasMany(p => p.Tags.Where(t => t.Active))`
- **When** `ExpressionHelper.GetPropertyName` runs
- **Then** `ArgumentException` is thrown with a message reading `does not refer to a property`

### Requirement: Flattening resolves each relationship through a data provider

The system SHALL, for each relationship of the definition, call
`IRelatedDataProvider.GetRelatedViaJunction` for `ManyToMany` and `GetRelated` otherwise, SHALL store the
result in `FlattenResult.NestedSingles` (taking `FirstOrDefault()`) for `OneToOne` and in
`NestedCollections` for every other type, SHALL reject a null root or provider with
`ArgumentNullException`, and SHALL reject a root with a null `Guid` with `ArgumentException`.

#### Scenario: A one-to-one relationship keeps only the first match

- **Given** a `OneToOne` relationship whose provider returns three child entities
- **When** `Flatten` runs
- **Then** `NestedSingles["DefaultImage"]` holds the first entity and the other two are discarded silently

#### Scenario: A root without a Guid cannot be flattened

- **Given** a root entity whose `Guid` is null
- **When** `new FlattenResult<T>(root)` is constructed
- **Then** `ArgumentException` is thrown reading `Root entity must have a non-null Guid.`

#### Scenario: Async flattening honours cancellation between relationships

- **Given** a definition with four relationships and a token cancelled after the first
- **When** `FlattenAsync` runs
- **Then** `ct.ThrowIfCancellationRequested()` at the top of the loop throws `OperationCanceledException`

#### Scenario: Typed collection access tolerates a loosely-typed provider

- **Given** the provider materialised children into a `List<AbstractModel>`
- **When** `GetCollection<Tag>("Tags")` is called
- **Then** the entries are filtered with `OfType<Tag>()` and returned, rather than a reference cast yielding null

#### Scenario: A missing navigation key reads as null, not empty

- **Given** no relationship named `"Missing"` was ever flattened
- **When** `GetCollection<Tag>("Missing")` is called
- **Then** null is returned

### Requirement: Expansion emits only Insert and Delete operations

The system SHALL diff a flattened aggregate against current state fetched from the provider and emit
`SyncOperation` values of type `Insert` and `Delete` only; it SHALL NOT emit
`SyncOperationType.Update`, so a field-level change to a child present in both current and desired state
produces no operation.

#### Scenario: An in-place child field change produces nothing

- **Given** a `OneToMany` relationship where current and desired both contain child `C` with the same `Guid` but different field values
- **When** `Expand` runs
- **Then** no `SyncOperation` is emitted for `C`

#### Scenario: A one-to-one swap becomes Delete plus Insert

- **Given** the current single child has `Guid = X` and the desired single child has `Guid = Y`
- **When** `ExpandSingle` runs
- **Then** two operations are emitted in order: `Delete` of `X` then `Insert` of `Y`, both tagged with the navigation property and `relationship.ChildType`

#### Scenario: Adding a one-to-one child where none existed

- **Given** current is null and desired is non-null
- **When** `ExpandSingle` runs
- **Then** a single `Insert` operation is emitted

#### Scenario: Clearing a one-to-one child

- **Given** current is non-null and the desired navigation key is absent from `NestedSingles`
- **When** `ExpandSingle` runs
- **Then** `TryGetValue` leaves the desired entity null and a single `Delete` of the current entity is emitted

### Requirement: Collection expansion inserts unkeyed children unconditionally

The system SHALL, for collection relationships, emit an `Insert` for every desired child whose `Guid` is
null before diffing, SHALL diff only the keyed desired children against current state by `Guid`, and SHALL
emit `Insert` for added keys and `Delete` for removed keys.

#### Scenario: Newly created children are not lost to key-based diffing

- **Given** a desired collection of three children, two with Guids already present in current state and one with a null Guid
- **When** `ExpandCollection` runs
- **Then** one `Insert` is emitted for the unkeyed child, and the keyed pair yields no operations

#### Scenario: A child removed from the aggregate is deleted

- **Given** current state holds children `X` and `Y` and the desired collection holds only `X`
- **When** `ExpandCollection` runs
- **Then** a `Delete` operation for `Y` is emitted

#### Scenario: An absent or null navigation collection deletes everything current

- **Given** current state holds two children and `NestedCollections` has no entry for the navigation property
- **When** `ExpandCollection` runs
- **Then** the desired set is treated as empty and `Delete` operations are emitted for both current children

### Requirement: Mapper construction requires the definition's root type to match

The system SHALL reject an `AggregateMapper<T>` whose definition has a `RootType` different from `T`, and
SHALL reject a null definition.

#### Scenario: Mismatched definition is rejected

- **Given** an `IAggregateDefinition` with `RootType = typeof(Order)`
- **When** `new AggregateMapper<Product>(definition)` is constructed
- **Then** `ArgumentException` is thrown reading `Definition root type 'Order' does not match mapper type 'Product'.`

### Requirement: Sync-pipeline extensions expose flatten and expand as batch helpers

The system SHALL provide `CreateMapper`, `FlattenForSync`/`FlattenForSyncAsync`,
`ExpandFromSync`/`ExpandFromSyncAsync` and `ExpandManyFromSync`/`ExpandManyFromSyncAsync` as delegating
extension methods over `IAggregateMapper<T>`, with the async many-variant checking cancellation before each
aggregate.

#### Scenario: Expanding many aggregates concatenates their operations

- **Given** three flattened aggregates each yielding two operations
- **When** `ExpandManyFromSync` runs
- **Then** the six operations are returned in aggregate order

#### Scenario: Async many-expansion is cancellable between aggregates

- **Given** ten aggregates and a token cancelled after the first
- **When** `ExpandManyFromSyncAsync` runs
- **Then** `OperationCanceledException` is thrown from `ct.ThrowIfCancellationRequested()` at the top of the loop

### Requirement: Sync operations and errors are immutable, validated records

The system SHALL reject a null `entityType`, `entity` or `navigationProperty` when constructing a
`SyncOperation` with `ArgumentNullException`, exposing all four members as get-only; and SHALL stamp every
`SyncError` with `Timestamp = DateTime.UtcNow` at construction.

#### Scenario: A sync operation cannot be built without an entity

- **Given** a call to `new SyncOperation(SyncOperationType.Insert, typeof(Tag), null, "Tags")`
- **When** the constructor runs
- **Then** `ArgumentNullException` is thrown for `entity`

#### Scenario: Errors are self-timestamping

- **Given** a `SyncError` created with only `Message` set
- **When** it is inspected
- **Then** `Timestamp` holds the UTC time of construction and `ItemGuid`, `Operation`, `Details` and `Exception` are null

### Requirement: Reserved and unused option surface

The system SHALL accept `SyncOptions.SkipPreview` without consulting it anywhere in the provider, and SHALL
accept `SyncFilterOptions<T>` in `DetermineSyncAction` without reading it; the sync provider's
`GetAllItems` SHALL likewise accept a `CancellationToken` and pass it to no store call.

#### Scenario: SkipPreview has no effect on a run

- **Given** two otherwise identical `SyncOptions`, one with `SkipPreview = true`
- **When** `Sync` runs with each
- **Then** the results are identical — `Preview` and `Sync` are independent entry points

#### Scenario: Synchronous fetch ignores the cancellation token

- **Given** an already-cancelled token in `options.CancellationToken`
- **When** `SyncProvider.GetAllItems` runs
- **Then** `store.Read(filter, null, null, null)` is invoked with no token, and cancellation is only observed at the next loop check

### Requirement: Fetch predicates narrow each side independently

The system SHALL pass `SyncFilterOptions<T>.LocalFetchPredicate` to the local store's read and
`RemoteFetchPredicate` to the remote store's read, and SHALL treat a null predicate as an unfiltered read.

#### Scenario: Asymmetric predicates change what counts as "missing"

- **Given** `LocalFetchPredicate = x => x.Active` and no `RemoteFetchPredicate`, with entity `G` present but inactive locally and present remotely
- **When** `Sync` runs with `Direction = Bidirectional`
- **Then** `G` is absent from `localDict`, so the decision path taken is the remote-only branch rather than the both-present branch

#### Scenario: No filter options reads everything

- **Given** `filterOptions` is null
- **When** `Sync` runs
- **Then** both stores are read with a null filter
