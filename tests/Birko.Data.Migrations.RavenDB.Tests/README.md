# Birko.Data.Migrations.RavenDB.Tests

Unit tests for the RavenDB migrations provider.

## Running Tests

```bash
dotnet test
```

Query/count paths (`CountDocuments`, `UpdateDocuments`, …) need a live RavenDB server and are not
covered by these unit tests. Building this project compile-verifies the provider's LINQ usings.

## License

Part of the Birko Framework.
