# Birko.Data.CosmosDB.ViewModel.Tests

Unit tests for the CosmosDB ViewModel repositories (no live Cosmos / emulator required).

## Running Tests

```bash
dotnet test
```

Covers the `AsyncCosmosDBRepository` constructor store-type guard (accepts a plain or wrapped
`AsyncCosmosDBStore`, rejects a foreign store), the unwrapping `CosmosStore` getter, and the
null-store `IsHealthy` no-op.

## License

Part of the Birko Framework.
