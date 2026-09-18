# Birko.Data.SQL.ViewModel

ViewModel repository implementations for SQL databases.

## Overview

Provides ViewModel-based repository implementations for SQL databases (SQL Server, PostgreSQL, MySQL, SQLite, TimescaleDB). Works with all SQL connectors from Birko.Data.SQL.

## Dependencies

- **Birko.Data.Core** - Models and filters
- **Birko.Data.Stores** - Store interfaces and settings
- **Birko.Data.Repositories** - Repository interfaces and abstractions
- **Birko.Data.ViewModel** - ViewModel base classes and interfaces
- **Birko.Data.SQL** - SQL base stores and connectors

## Files

- **Repositories/IDataBaseRepository.cs** - `IDataBaseRepository<TConnector, TViewModel, TModel>` interface
- **Repositories/DataBaseRepository.cs** - Sync ViewModel repository (`DataBaseRepository<TConnector, TViewModel, TModel>`)
- **Repositories/AsyncDataBaseRepository.cs** - Async ViewModel repository (`AsyncDataBaseRepository<TViewModel, TModel>`)

## Notes

- Works with all SQL implementations (MSSql, PostgreSQL, MySQL, SQLite, TimescaleDB)
- Integrates with Birko.Data.Tenant for multi-tenancy
- Model-direct repositories (DataBaseModelRepository, AsyncDataBaseModelRepository) remain in Birko.Data.SQL

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
