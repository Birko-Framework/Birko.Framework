# Birko.Communication.WebSocket.Tests

## Overview
Unit tests for the Birko.Communication.WebSocket project - WebSocket communication and settings tests.

## Project Location
`C:\Source\Birko.Communication.WebSocket.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `WebSocketSettingsTests.cs` - WebSocket settings configuration tests

## Dependencies
- Birko.Communication.WebSocket (via .projitems) - WebSocket communication
- Birko.Communication (via .projitems) - communication abstractions
- Birko.Security (via .projitems) - security support
- Birko.Data.Core, Birko.Data.Stores (via .projitems) - data layer
- Birko.Contracts, Birko.Configuration (via .projitems) - core contracts and configuration

## Running Tests
```bash
dotnet test Birko.Communication.WebSocket.Tests.csproj
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
