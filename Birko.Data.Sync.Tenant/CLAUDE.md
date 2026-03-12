# Birko.Data.Sync.Tenant

## Overview
Tenant-aware synchronization support for multi-tenant applications using the Birko.Data.Sync framework.

## Project Location
`C:\Source\Birko.Data.Sync.Tenant\`

## Components

### Interfaces
- `ITenantSyncKnowledgeItem` - Extends `ISyncKnowledgeItem` and `ITenant`

### Sync Provider
- `TenantSyncProvider<TStore, T>` - Implements `ISyncProvider` with tenant scoping
  - `PreviewAsync()`, `SyncAsync()` methods
  - `ApplyTenantContext()`, `ApplyTenantFiltering()` for tenant isolation
  - `BelongsToTenant()`, `DetermineSyncAction()`, `ResolveConflict()` logic

### Queue
- `TenantSyncQueue` - Extends `SyncQueue` with tenant-aware scoping
  - `GetEffectiveTenantId()`, `GetQueueKey()` overrides

### Models
- `TenantSyncResult` - Extends `SyncResult` with `TenantId` and `TenantName`
- `TenantSyncOptions` - Extends `SyncOptions` with `TenantId` and `TenantName`

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
