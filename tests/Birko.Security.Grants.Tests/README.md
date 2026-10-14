# Birko.Security.Grants.Tests

Unit tests for `Birko.Security.Grants`: the role table, `CanAsync`, grant / revoke rules, the last-owner rule under
concurrency, tenant scoping and listing.

## Test framework

xUnit 2.9 + FluentAssertions 7, against `Birko.Data.InMemory` stores.

## Running tests

```
dotnet test --nologo
```

## License

See the repository root `LICENSE`.
