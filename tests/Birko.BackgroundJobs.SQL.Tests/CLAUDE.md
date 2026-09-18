# Birko.BackgroundJobs.SQL.Tests

## Overview
Unit tests for the Birko.BackgroundJobs.SQL project - SQL-based background job persistence and scheduling tests.

## Project Location
`C:\Source\Birko.BackgroundJobs.SQL.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `Models/JobDescriptorModelTests.cs` - JobDescriptor model tests

## Dependencies
- Birko.BackgroundJobs.SQL (via .projitems) - SQL background job stores
- Birko.BackgroundJobs (via .projitems) - background job abstractions
- Birko.Data.SQL, Birko.Data.SQL.View (via .projitems) - SQL data access
- Birko.Data.Core, Birko.Data.Stores, Birko.Data.Repositories, Birko.Data.Patterns (via .projitems) - data layer
- Birko.Rules, Birko.Serialization, Birko.Models, Birko.Models.Contracts (via .projitems)
- Birko.Contracts, Birko.Time, Birko.Configuration (via .projitems)

## Running Tests
```bash
dotnet test Birko.BackgroundJobs.SQL.Tests.csproj
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
