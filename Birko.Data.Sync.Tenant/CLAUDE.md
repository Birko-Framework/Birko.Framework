# Birko.Data.Sync.Tenant

## Overview
Tenant-aware synchronization support for multi-tenant applications using the Birko.Data.Sync framework.

## Project Location
`Birko.Data.Sync.Tenant/`

## Components

### Interfaces
- `ITenantSyncKnowledgeItem` - Extends `ISyncKnowledgeItem` and `ITenant`

### Sync Provider
- `TenantSyncProvider<TStore, T>` - Implements `ISyncProvider` with tenant scoping
  - `PreviewAsync()`, `SyncAsync()` methods
  - `ResolveTenantScope()`, `ApplyTenantFiltering()`, `BuildTenantPredicate()` for tenant isolation
  - `BelongsToTenant()`, `DetermineSyncAction()`, `ResolveConflict()` logic

#### Tenant scoping — the fetch is what is scoped (SH-H050/H051/H052)

The tenant term goes on the **fetch** predicates, not just the save predicates. It used to be the other way
round, and everything else followed from that: every tenant's rows entered `localDict`/`remoteDict`, so
`PreviewAsync` under tenant *t* enumerated and version-hashed another tenant's entities, their guids reached
the knowledge store, and the `SyncAction.Delete` arm — which consulted no predicate at all, unlike
Create/Update — **deleted another tenant's rows**. Scoping the fetch makes the read, compare, preview,
delete and knowledge paths correct by construction instead of each needing its own guard.

Three rules for anyone touching this provider:

- **One tenant per run, resolved once.** `ResolveTenantScope` is the only place that answers "which tenant";
  the fetch predicates, save predicates, knowledge keys and knowledge items all take its return value. Two
  live answers *was* the bug (`options.TenantGuid` for knowledge, the ambient tenant for saves), so
  `SyncAsync(new TenantSyncOptions { TenantGuid = u })` with no ambient tenant installed **no save predicate
  at all**.
- **Contradiction is refused, absence is refused.** Ambient *t* plus an explicit *u* throws
  `TenantMismatchException` rather than picking a winner; a tenant-scoped entity with no tenant from either
  source throws `TenantScopeRequiredException` rather than syncing everything. `IsAllTenantsScope` is the one
  sanctioned cross-tenant path, and under it an explicit `options.TenantGuid` still narrows the run — that is
  the per-tenant admin loop.
- **The post-fetch `BelongsToTenant` pass in `GetAllItemsAsync` is not redundant.** A fetch predicate is only
  as strong as the backend's translation of it, and this family has shipped filters a backend silently
  widened to match-all (a NEST request with a null `Query`; an empty `IN` rendered always-true). Deleting
  that pass moves the tenant guarantee back into the backend's hands.

### Queue
- `TenantSyncQueue` - Extends `SyncQueue` with tenant-aware scoping
  - `GetEffectiveTenantGuid()`, `GetQueueKey()` overrides

### Models
- `TenantSyncResult` - Extends `SyncResult` with `TenantGuid` and `TenantName`
- `TenantSyncOptions` - Extends `SyncOptions` with `TenantGuid` and `TenantName`

### Extensions
- `TenantSyncProviderExtensions` - `CreateTenantSync()`, `WithTenantSync()` helpers
- `TenantSyncSetup<TStore, T>` - Fluent setup class

## Dependencies
- Birko.Data.Sync
- Birko.Data.Tenant

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
