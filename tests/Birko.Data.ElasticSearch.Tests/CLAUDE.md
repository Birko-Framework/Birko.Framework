# Birko.Data.ElasticSearch.Tests

## Overview
Unit tests for the Birko.Data.ElasticSearch project - expression parsing and query building.

## Project Location
`C:\Source\Birko.Data.ElasticSearch.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `ExpressionTests.cs` - Elasticsearch expression parsing tests (TermQuery, RangeQuery, nested Any)
- `TestResources/Models/DateModel.cs` - Test fixture model

## Dependencies
- Birko.Data (via .projitems) - core models and interfaces
- Birko.Data.ElasticSearch (via .projitems) - Elasticsearch expression parser
- Birko.Data.SQL (via .projitems) - SQL expression parser (used in cross-reference test)
- Birko.Data.SQL.View (via .projitems) - SQL view generation
- Birko.Models (via .projitems) - model base classes
- Birko.Data.Patterns (via .projitems) - UnitOfWork interface
- NEST / Elasticsearch.Net - Elasticsearch client libraries

## Known Issues
- `ParseAnyExpression` test fails - pre-existing issue with nested Any() expression parsing

## Running Tests
```bash
dotnet test Birko.Data.ElasticSearch.Tests.csproj
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
