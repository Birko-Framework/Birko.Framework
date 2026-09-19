# Birko.Data.Sync.Tests

## Overview
Unit tests for the Birko.Data.Sync project - data synchronization provider and queue tests.

## Project Location
`tests/Birko.Data.Sync.Tests/`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `SyncProviderTests.cs` - SyncProvider tests (initial/download/upload)
- `SyncQueueTests.cs` - SyncQueue tests (serialization, concurrency)
- `Models/SyncResultTests.cs` - SyncResult model tests
- `TestInfrastructure/TestSyncModel.cs` - Test sync model
- `TestInfrastructure/TestSyncKnowledge.cs` - Test sync knowledge
- `TestInfrastructure/TestBulkStore.cs` - Test bulk store
- `TestInfrastructure/TestSyncKnowledgeItemStore.cs` - Test sync knowledge item store

## Dependencies
- Birko.Data.Sync (via .projitems) - data synchronization
- Birko.Data.Core, Birko.Data.Stores (via .projitems) - data layer
- Birko.Contracts, Birko.Time, Birko.Configuration (via .projitems) - core contracts

## Running Tests
```bash
dotnet test Birko.Data.Sync.Tests.csproj
```

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
