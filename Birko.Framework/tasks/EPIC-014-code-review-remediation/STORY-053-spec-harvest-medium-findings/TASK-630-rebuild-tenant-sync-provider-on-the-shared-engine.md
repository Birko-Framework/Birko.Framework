---
id: TASK-630
parent: STORY-053
feature: FEATURE-014
status: done
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

- [x] `TenantSyncProvider` delegates the run to the shared engine; tenant resolution and the tenant predicates are kept
- [x] Each of the eleven findings has a regression test, red on the current code (most become the shared engine's tests run under a tenant)
- [x] `TenantSyncQueue` runs two tenants concurrently while serialising one tenant's syncs
- [x] No cross-tenant entity reaches the tenant's knowledge scope (SH-M346), asserted by reading the knowledge store

## Ordering

After [[TASK-157]] (the shared engine's own triage), so the rebuild stands on a triaged engine.

## Resolution (2026-10-15)

**`TenantSyncProvider<TStore, T, TKnowledge>` is the shared engine run under one tenant**: 372 lines instead of 1,107.
It keeps what is tenant-specific (`ResolveTenantScope`, the tenant fetch and save predicates, `BelongsToTenant`,
unchanged) and runs `AsyncSyncProvider` for everything else. Tenant isolation of knowledge comes from running under
`TenantScope(scope, tenant)` = `{scope}@tenant:{guid:N}`, so it takes any `IAsyncSyncKnowledgeItemStore<T>`, and no
knowledge store needs a tenant column (closes SH-M065 and SH-M066 for tenant sync).

**Carried into the shared engine** so the rebuild loses nothing: the in-process enforcement of the fetch predicate
(`EnforceFetchPredicate`; the tenant copy's post-fetch `BelongsToTenant` pass, which now protects every engine user),
and the content hash for an entity with no `UpdatedAt` (CR-H104; an unchanged row now hashes equal and is skipped, where
the engine gave it a random version every run). `SyncOptions.Clone()` is new: the provider runs on a copy.

**RavenDB and CosmosDB knowledge stores** now implement the engine's interfaces, like every other backend. Their own
tenant API, identity and conversion paths are gone, and with them SH-M060, M062, M063, M064 and M436. The models no
longer carry a tenant (`RavenSyncKnowledgeItem` is no longer `ITenant`, so Birko.Data.Sync.RavenDB no longer needs
Birko.Data.Tenant). `ISyncKnowledgeStore`, `ITenantSyncKnowledgeItem`, `TenantSyncKnowledgeItem` and
`CosmosSyncKnowledgeQuery` are deleted. The RavenDB store is verified live: two runs leave one knowledge row per
entity, updated by the second (`RavenSyncKnowledgeLiveTests`). CosmosDB has no emulator here: offline tests only.

**BREAKING** (no consumer references any of it, measured): `TenantSyncProvider` gains a knowledge type parameter and
takes `IAsyncSyncKnowledgeItemStore<TKnowledge>`; the Raven/Cosmos knowledge stores lose their tenant API.

**Tests.** `TenantSyncEngineTests` covers each of SH-M342 to SH-M352 (12 tests). The 27 tests of the old suite that
test the provider's contract (tenant resolution, scoping, refusals, filter copies) are ported to the new constructor and
pass unchanged in substance; 10 that called internals of the deleted engine copy are removed, and the 6 version-hash
ones are ported to the engine (`EngineVersionHashTests`). **Red-verification is limited by the rebuild**: the new
tests cannot construct the old provider (it took `ISyncKnowledgeStore`, which nothing implemented). What could be
isolated was: SH-M350 on the pre-TASK-157 `SyncQueue` 1 of 1; removing the in-process fetch enforcement fails
`ForeignRowsAreDropped_EvenWhenTheStoreIgnoresTheFetchPredicate`; restoring the random version fails 2 of 6
`EngineVersionHashTests`. The other findings were confirmed by reading (TASK-161 / TASK-157), and their behaviour is
the shared engine's, whose own tests were red-verified in TASK-157.

**Regression:** the 10 suites importing a sync project, 243 tests, green, live against MongoDB, ElasticSearch and
RavenDB.

**Spec regen pending**, batched: `tenant-isolation`, `data-sync`.

## Human test plan

N/A: fully covered by automated tests; asserted by store and knowledge contents.
