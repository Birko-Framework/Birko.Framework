# Birko.Data.Tests

Unit tests for the core Birko.Data.Core and Birko.Data.Stores projects.

## Test Coverage

- **AsyncStoreTests** - CRUD operations, Save, Init, Destroy, CancellationToken support

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0
- .NET 10.0

## Running Tests

```bash
dotnet test Birko.Data.Tests
```

## Dependencies

- Birko.Data.Core (shared project via .projitems)
- Birko.Data.Stores (shared project via .projitems)

## License

Part of the Birko Framework.
