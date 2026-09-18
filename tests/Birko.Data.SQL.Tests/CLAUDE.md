# Birko.Data.SQL.Tests

## Overview
Unit tests for the Birko.Data.SQL project - SQL connector condition builders, strategies, and expression parsing.

## Project Location
`C:\Source\Birko.Data.SQL.Tests\`

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Moq 4.20.72 (for AbstractConnector mocking only)
- coverlet.collector 6.0.2

## Test Structure

### TestHelpers
- `TestDbCommand.cs` - Concrete DbCommand stub (DbCommand.Parameters is non-virtual in .NET 10)
- `TestDbParameter.cs` - Concrete DbParameter stub
- `TestDbParameterCollection.cs` - Concrete DbParameterCollection stub with `All` property for assertions

### TestResources
- `Models/DateModel.cs` - Test fixture model (DateModel, NestedDateModel)

### Connectors
- `SqlBuilderContextTests.cs` - SQL builder context utilities (parameter names, value formatting, escaping)
- `AbstractConnectorConditionBuilderTests.cs` - SQL condition building (AND/OR, sub-conditions, field comparisons)

### Strategies
- `ComparisonConditionStrategyTests.cs` - Comparison operators (>, <, >=, <=)
- `EqualConditionStrategyTests.cs` - Equality conditions (=, <>)
- `InConditionStrategyTests.cs` - IN/NOT IN clause conditions
- `LikeConditionStrategyTests.cs` - LIKE pattern matching (StartsWith, EndsWith)
- `NullConditionStrategyTests.cs` - IS NULL/IS NOT NULL conditions

### Views
- `Views/ViewDdlTests.cs` - View DDL SQL generation (LoadView metadata, JOIN types, BuildViewSelectSql, BuildCreateViewSql per-provider syntax, aggregate fields, error cases)

### TestResources/Views
- `Views/CustomerOrderView.cs` - Test view fixtures (INNER JOIN with COUNT/SUM, LEFT OUTER JOIN with COUNT)

### DataBase (Expression Parsing)
- `DataBase/ExpressionTests.cs` - SQL expression parsing (fields, values, arithmetic, functions)
- `DataBase/WhereExpressionTests.cs` - WHERE condition expression parsing

## Dependencies
- Birko.Data.Core (via .projitems) - core models and filters
- Birko.Data.Stores (via .projitems) - store interfaces and settings
- Birko.Data.SQL (via .projitems) - SQL connectors, strategies, expression parser
- Birko.Data.SQL.View (via .projitems) - SQL view generation
- Birko.Models (via .projitems) - model base classes
- Birko.Data.Patterns (via .projitems) - UnitOfWork interface

## Known Issues
- `WhereExpressionTests.ParseCombinedExpression` fails with NullReferenceException in DataBase.ParseConditionExpression - pre-existing bug in expression parser for combined AND expressions

## Running Tests
```bash
dotnet test Birko.Data.SQL.Tests.csproj
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
