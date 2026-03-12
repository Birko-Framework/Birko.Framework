# Birko.Data.SQL.Tests

Unit tests for SQL connectors, strategies, and expression parsing in the Birko Framework.

## Test Coverage

- **Connectors/** - SQL connector tests
- **Strategies/** - SQL builder context, condition builders
- **ExpressionTests** - SQL expression parsing

## Test Helpers

- **TestDbCommand** / **TestDbParameter** / **TestDbParameterCollection** - Stubs for DbCommand.Parameters (non-virtual in .NET 10)
- **TestResources/DateModel** - Test fixture

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0
- .NET 10.0

## Running Tests

```bash
dotnet test Birko.Data.SQL.Tests
```

## Dependencies

- Birko.Data.SQL (shared project via .projitems)

## License

Part of the Birko Framework.
