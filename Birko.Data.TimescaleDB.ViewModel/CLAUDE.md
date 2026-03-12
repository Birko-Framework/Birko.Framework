# Birko.Data.TimescaleDB.ViewModel

## Overview
ViewModel repository implementations for TimescaleDB stores.

## Project Location
`C:\Source\Birko.Data.TimescaleDB.ViewModel\`

## Components

### Repositories
- `TimescaleDBRepository<TViewModel, TModel>` - Sync ViewModel repository extending DataBaseRepository with TimescaleDBConnector
- `AsyncTimescaleDBRepository<TViewModel, TModel>` - Async bulk ViewModel repository
  - `SetSettings()` (3 overloads for different settings types)
  - `InitAsync()`, `DropAsync()`, `CreateSchemaAsync()`, `CreateHypertableAsync()` methods

### Base Classes
- Extends `DataBaseRepository<TimescaleDBConnector>` (sync)
- Extends `AbstractAsyncBulkViewModelRepository` (async)

## Dependencies
- Birko.Data
- Birko.Data.SQL
- Birko.Data.TimescaleDB
- Birko.Data.ViewModel

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
