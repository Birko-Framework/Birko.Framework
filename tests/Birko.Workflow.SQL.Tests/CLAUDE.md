# Birko.Workflow.SQL.Tests

## Overview
Unit tests for the Birko.Workflow.SQL project - SQL-based workflow engine persistence tests.

## Project Location
`C:\Source\Birko.Workflow.SQL.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `Models/WorkflowInstanceModelTests.cs` - WorkflowInstance model tests

## Dependencies
- Birko.Workflow.SQL (via .projitems) - SQL workflow stores
- Birko.Workflow (via .projitems) - workflow engine abstractions
- Birko.Data.SQL, Birko.Data.SQL.View (via .projitems) - SQL data access
- Birko.Data.Core, Birko.Data.Stores, Birko.Data.Repositories, Birko.Data.Patterns (via .projitems) - data layer
- Birko.Rules, Birko.Models, Birko.Models.Contracts (via .projitems)
- Birko.Contracts, Birko.Time, Birko.Configuration (via .projitems) - core contracts

## Running Tests
```bash
dotnet test Birko.Workflow.SQL.Tests.csproj
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
