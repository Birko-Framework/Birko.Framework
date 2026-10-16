---
id: TASK-650
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Each refuses or agrees, with a test
- [ ] Red on the current code; the spec `docs/specs/tenant-isolation.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-650` — leave empty until then._
