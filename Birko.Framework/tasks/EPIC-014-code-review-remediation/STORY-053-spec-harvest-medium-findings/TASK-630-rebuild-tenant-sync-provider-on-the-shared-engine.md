---
id: TASK-630
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: [TASK-157]
blocks: []
findings: [SH-M342, SH-M343, SH-M344, SH-M345, SH-M346, SH-M347, SH-M348, SH-M349, SH-M350, SH-M351, SH-M352, SH-M060, SH-M062, SH-M063, SH-M064, SH-M065, SH-M066, SH-M436]
pr: null
github-issue: null
jira-key: null
---

# `TenantSyncProvider` is a second sync engine, and it has drifted from the first in eleven ways

## Context

Spawned by [[TASK-161]] (2026-10-15). `Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs` (1,107 lines)
re-implements the batch loop, the action decision, conflict resolution, preview and knowledge bookkeeping that
`Birko.Data.Sync`'s `AsyncSyncProvider` / `SyncProviderBase` already provide. Eleven of the tenant-isolation medium
findings are places where the copy diverged, each confirmed by reading on 2026-10-15:

- SH-M342 per-batch errors are cumulative; SH-M343 a Bidirectional `Create` writes nothing yet counts as processed and
  records knowledge (the default direction drops every one-sided create); SH-M344 preview decides independently of the
  sync and never reports a conflict; SH-M345 a tenant-blocked update is uncounted; SH-M346 knowledge is recorded for
  skipped writes, stamping other tenants' entity guids into this tenant's scope; SH-M347 the last-sync time is written
  after a cancelled or failed run; SH-M348 `OnSaveFilterBlock` / `OnError` are ignored; SH-M349 `MaxItems` and
  `OnBatchStarting` are ignored; SH-M351 a duplicate or unset guid aborts the run as "Sync failed"; SH-M352 a mismatched
  `filterOptions` is dropped by `as`.
- SH-M350 `TenantSyncQueue`: the per-tenant key scopes only the bookkeeping; one semaphore serialises every tenant.

(SH-M340 and SH-M341, the option mutations, were already fixed: both are copied now.)

### Handed over by [[TASK-157]] (2026-10-15)

TASK-157 triaged the shared engine first, as this task depends on, and changed what it stands on:

- **The engine's decision is now built on a known pair** (a knowledge row recorded while both sides held the item):
  deletion is "known pair, one side missing", versions are compared, knowledge is written only for a reconciled state,
  and each run writes a scope marker row (`EntityGuid = Guid.Empty`). The tenant copy still reads the stored
  `IsLocalDeleted` / `IsRemoteDeleted` flags, which measured as resurrecting deletions and deleting first-sync local
  items in the shared engine before the fix. Rebuilding on the engine inherits the fix.
- **SH-M350 is fixed already**: `SyncQueue` now gates per queue key, and `TenantSyncQueue` enqueues through
  `EnqueueWithKeyAsync` with its tenant key, so tenants no longer serialise behind one another. Verify and close it here.
- **The Raven and Cosmos knowledge stores** implement neither knowledge-store interface; `TenantSyncProvider` is their
  only consumer. Their findings move here, because rebuilding means giving them the engine's
  `IAsyncSyncKnowledgeItemStore<T>` shape (tenant-scoped), and fixing their current shape would be work thrown away:
  SH-M060 (Cosmos writes are no-ops with no container), SH-M062 / SH-M063 (Raven's deterministic Guid never becomes the
  document id; its own type skips the derivation), SH-M064 (their `ToDictionary` throws on a duplicate EntityGuid; the
  engine side is fixed), SH-M065 (`ISyncKnowledgeStore` has no implementation), SH-M066 (knowledge is tenant-blind in
  the five generic backends: the engine needs a tenant dimension), SH-M436 (Cosmos keeps a foreign `Guid.Empty`).
- **Interface members in filters are fixed in the shared normaliser** (TASK-157): a filter written against an interface
  in generic code now reaches SQL / ElasticSearch / RavenDB with the concrete model's mapping. The generic engine over
  ElasticSearch never found its knowledge before this.

## Why a rebuild, not eleven patches

Every one of these is "the copy does not do what the original does". Patching them keeps two engines, and the next
change to the shared engine (TASK-157 triages it) would have to be made twice. Tenant scoping needs only what the shared
engine already takes: fetch and save predicates through `SyncFilterOptions<T>`, and a knowledge store whose scope
includes the tenant. Built on `AsyncSyncProvider`, the tenant provider shrinks to tenant resolution (TASK-114's
`ResolveTenantScope`, kept), predicate composition, and a tenant-keyed knowledge adapter.

## Acceptance criteria

- [ ] `TenantSyncProvider` delegates the run to the shared engine; tenant resolution and the tenant predicates are kept
- [ ] Each of the eleven findings has a regression test, red on the current code (most become the shared engine's tests run under a tenant)
- [ ] `TenantSyncQueue` runs two tenants concurrently while serialising one tenant's syncs
- [ ] No cross-tenant entity reaches the tenant's knowledge scope (SH-M346), asserted by reading the knowledge store

## Ordering

After [[TASK-157]] (the shared engine's own triage), so the rebuild stands on a triaged engine.

## Human test plan

N/A: asserted by store and knowledge contents.
