# Birko.Data.Tests

## Overview
Unit tests for the core Birko data layer - store and repository abstractions.

## Project Location
`C:\Source\Birko.Data.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `Stores/AsyncStoreTests.cs` - AbstractAsyncStore implementation tests (CRUD, Save, Init, Destroy, CancellationToken)
- `Paging/PagedRepositoryWrapperTests.cs` - Sync paged repository wrapper tests
- `Paging/AsyncPagedRepositoryWrapperTests.cs` - Async paged repository wrapper tests
- `Specification/SpecificationTests.cs` - Specification pattern tests (And, Or, Not, operators, LINQ)
- `Concurrency/VersionedStoreWrapperTests.cs` - Sync versioned store wrapper tests
- `Concurrency/AsyncVersionedStoreWrapperTests.cs` - Async versioned store wrapper tests

## Dependencies
- Birko.Data.Core, Birko.Data.Stores, Birko.Data.Repositories (via .projitems) - core models, store interfaces, and repository abstractions
- Birko.Data.Patterns (via .projitems) - paging, specification, concurrency patterns

## Running Tests
```bash
dotnet test Birko.Data.Tests.csproj
```

## Note
Previously this project contained tests for SQL expressions, Elasticsearch, Helpers, and Structures.
These have been split into dedicated test projects:
- Birko.Data.SQL.Tests - SQL expression/condition tests
- Birko.Data.ElasticSearch.Tests - Elasticsearch expression tests
- Birko.Helpers.Tests - Helper utility tests
- Birko.Structures.Tests - Data structure tests

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
