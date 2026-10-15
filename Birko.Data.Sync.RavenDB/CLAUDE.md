# Birko.Data.Sync.RavenDB

## Overview
RavenDB-specific sync knowledge item and store implementations for the Birko.Data.Sync framework.

## Project Location
`Birko.Data.Sync.RavenDB/`

## Components

### Models
- `RavenSyncKnowledgeItem` - Extends `AbstractModel`, implements `ISyncKnowledgeItem` with scope validation

### Stores
- `RavenSyncKnowledgeStore` - `RavenDBStore<RavenSyncKnowledgeItem>` implementing `ISyncKnowledgeItemStore<>`
- `AsyncRavenSyncKnowledgeStore` - `AsyncRavenDBStore<RavenSyncKnowledgeItem>` implementing `IAsyncSyncKnowledgeItemStore<>`
  - TASK-630: both used to expose their own tenant API (`GetKnowledgeAsync(scope, tenantId)` and friends) with their own
    identity path, which stored a new document every round (SH-M062/M063). They now give the engine what every backend
    gives it, and the engine upserts by Guid. Verified live (`RavenSyncKnowledgeLiveTests`).

## Dependencies
- Birko.Data.Sync
- Birko.Data.RavenDB
- (No longer Birko.Data.Tenant, TASK-630: the model carries no tenant; the tenant provider keeps tenants apart by
  scope.)

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
