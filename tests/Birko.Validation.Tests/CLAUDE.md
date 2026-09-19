# Birko.Validation.Tests

## Overview
Unit tests for the Birko.Validation project - validation rules, fluent validators, and store wrapper integration tests.

## Project Location
`tests/Birko.Validation.Tests/`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Test Structure
- `Rules/RequiredRuleTests.cs` - Required validation rule tests
- `Rules/EmailRuleTests.cs` - Email validation rule tests
- `Rules/LengthRuleTests.cs` - Length validation rule tests
- `Rules/RangeRuleTests.cs` - Range validation rule tests
- `Rules/RegexRuleTests.cs` - Regex validation rule tests
- `Rules/CustomRuleTests.cs` - Custom validation rule tests
- `Core/ValidationResultTests.cs` - ValidationResult model tests
- `Core/ValidationExceptionTests.cs` - ValidationException tests
- `Core/ValidationContextTests.cs` - ValidationContext tests
- `Fluent/AbstractValidatorTests.cs` - Fluent AbstractValidator tests
- `Integration/ValidatingStoreWrapperTests.cs` - Sync validating store wrapper tests
- `Integration/AsyncValidatingStoreWrapperTests.cs` - Async validating store wrapper tests
- `Integration/AsyncValidatingBulkStoreWrapperTests.cs` - Async bulk validating store wrapper tests

## Dependencies
- Birko.Validation (via .projitems) - validation framework
- Birko.Rules (via .projitems) - rules engine
- Birko.Data.Core, Birko.Data.Stores (via .projitems) - data layer
- Birko.Contracts, Birko.Time, Birko.Configuration (via .projitems) - core contracts

## Running Tests
```bash
dotnet test Birko.Validation.Tests.csproj
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
