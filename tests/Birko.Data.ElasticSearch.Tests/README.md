# Birko.Data.ElasticSearch.Tests

Unit tests for Elasticsearch expression parsing in the Birko Framework.

## Test Coverage

- **ExpressionTests** - TermQuery, RangeQuery, nested Any() expressions

## Known Issues

- ParseAnyExpression test fails with nested Any() expressions

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0
- .NET 10.0

## Running Tests

```bash
dotnet test Birko.Data.ElasticSearch.Tests
```

## License

Part of the Birko Framework.
