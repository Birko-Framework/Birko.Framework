---
id: TASK-650
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Edge cases: tenant sync without a Guid tenant, Cosmos empty partition path, Cosmos index verbs disagree, sync ES init NRE

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `tenant-isolation`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

Four small read-only findings, one task because each is a guard:
- `TenantSyncProvider`: a `TenantGuid` that is neither `Guid` nor `Guid?` gives no fetch predicate, so every tenant's
  rows enter preview, comparison and knowledge (writes still blocked), although the comment says fail-closed
  (`Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:226,284`).
- Cosmos `RequireAddressablePartitionKeyPath` accepts `""`, but container creation falls back only on null
  (`Birko.Data.CosmosDB/Stores/Settings.cs:34`, `AsyncCosmosDBStore.cs:580`, `CosmosDBStore.cs:446`).
- Cosmos index manager: for an unregistered composite element, `ExistsAsync` is true by path while `GetInfoAsync` is null
  and `DropAsync` does nothing; `ExistsAsync` also creates the registry container (`CosmosDBIndexManager.cs:77,240,262`).
- Sync `ElasticSearchStore.Init` dereferences `Connector!` with no check, so a store with no client throws
  NullReferenceException before `RequireConnector` (`Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs:92`); the
  async twin throws InvalidOperationException.

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Each refuses or agrees, with a test
- [x] Red on the current code; the spec `docs/specs/tenant-isolation.md` updated if behaviour changes

## Resolution (2026-10-17)

Three of the four reproduced; one was refuted.

1. **Tenant sync: reproduced and fixed.** A model whose `TenantGuid` is a `string`, synced with a tenant in scope,
   previewed and synced without complaint. Nothing could be fetched by tenant, so every tenant's rows entered the
   run. `ApplyTenantFiltering` now refuses a scoped run with `NotSupportedException` naming the type, and an
   explicit `WithAllTenantsAsync` run is unchanged (pinned). The remark claiming the run stayed fail-closed is
   corrected.
2. **Cosmos partition path: reproduced and fixed.** Validation accepted `""` as the default, while container creation
   fell back to `/id` only on `null`, and Cosmos refuses a container on an empty path (measured on the emulator). The
   new `Settings.EffectivePartitionKeyPath` is the one producer used by both stores' creation. Red check: with the
   old null-only fallback restored inside it, the `""` case failed.
3. **Cosmos index verbs: reproduced live, 2 red.** On a container whose policy held an unregistered composite
   `(/A, /B)`, `ExistsAsync("A")` was true while `GetInfoAsync` found nothing and `DropAsync` removed nothing. Also,
   `ExistsAsync` on a fresh database created `__birko_indexes`. An unregistered name now answers by single-field
   included paths on all three verbs. A composite is named only through TASK-628's registry. CR-M085's pin
   `PolicyContainsIndex_finds_a_composite_index_element_by_name` is inverted, not deleted (rule 56).
   `ReadRegistryAsync` reads through `GetContainer`, where an absent container is a 404, the same as an absent
   document; only `UpdateRegistryAsync` creates it.
4. **ElasticSearch Init NRE: refuted.** `Init(descriptor)` does dereference `Connector!` before `RequireConnector`,
   but cannot get there with no client. Without settings, `GetIndexName()` throws first ("Settings not initialized.
   Call SetSettings() first."), and `SetSettings` refuses any settings that would leave no client (SH-M146). No guard
   was added for an unreachable state (rule 53). Both answers are pinned.

**Tests:** `Task650TenantGuidTypeTests` (2), `Task650CosmosGuardsTests` (6; 3 live on the Cosmos emulator, Gateway),
and `Task650ElasticInitTests` (2 pins). The suites: Sync.Tenant 41, CosmosDB 123 and ElasticSearch 287, live. The 8
suites that import `Birko.Data.CosmosDB` or `Birko.Data.Sync.Tenant` (263 tests) are green.

**Docs:** the specs `tenant-isolation`, `schema-index-and-ddl` (the Cosmos index manager) and
`settings-configuration-chain`, and Recent Updates.

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-650` — leave empty until then._
