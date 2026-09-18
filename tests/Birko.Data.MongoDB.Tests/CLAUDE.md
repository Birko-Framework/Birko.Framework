# Birko.Data.MongoDB.Tests

## Overview
Unit tests for Birko.Data.MongoDB — aggregation pipeline building and change stream options.

## Project Location
`C:\Source\Birko.Data.MongoDB.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Moq 4.20.72
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `Aggregation/AggregationPipelineBuilderTests.cs` - Tests for aggregation pipeline builder
- `ChangeStreams/ChangeStreamOptionsTests.cs` - Tests for change stream options

## Dependencies
- Birko.Data.Core (via .projitems) - core models and filters
- Birko.Data.Stores (via .projitems) - store interfaces and settings
- Birko.Data.Repositories (via .projitems) - repository interfaces
- Birko.Data.MongoDB (via .projitems) - MongoDB store/repository implementation
- Birko.Data.Patterns (via .projitems) - cross-cutting patterns
- Birko.Rules (via .projitems) - rule engine
- Birko.Time (via .projitems) - time abstractions

## Running Tests
```bash
dotnet test Birko.Data.MongoDB.Tests.csproj
```

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
