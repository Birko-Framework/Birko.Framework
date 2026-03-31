# Birko.Caching.Tests

## Overview
Unit tests for the Birko.Caching project - caching abstractions and memory cache implementation tests.

## Project Location
`C:\Source\Birko.Caching.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `Core/CacheResultTests.cs` - CacheResult model tests
- `Core/CacheEntryOptionsTests.cs` - CacheEntryOptions configuration tests
- `Memory/MemoryCacheTests.cs` - In-memory cache implementation tests

## Dependencies
- Birko.Caching (via .projitems) - caching abstractions and implementations
- Birko.Serialization (via .projitems) - serialization support

## Running Tests
```bash
dotnet test Birko.Caching.Tests.csproj
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
