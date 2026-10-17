# Birko.Health.Security.Grants

A health check that reports stored resource grants whose role or resource type is no longer declared.

## Features
- `StaleGrantHealthCheck`: Healthy when every stored grant names a declared role and resource type; Degraded, with the
  count and descriptions, when some do not; Degraded when the grant store cannot be read
- The reserved admin grant is never reported; nothing is migrated or revoked

## Usage

```csharp
// After AddBirkoGrants(...), which registers the IResourceGrantStore and the GrantRoleTable:
var check = new StaleGrantHealthCheck(
    sp.GetRequiredService<IResourceGrantStore>(),
    sp.GetRequiredService<GrantRoleTable>());
```

## Running tests

```
dotnet test tests/Birko.Health.Security.Grants.Tests
```

## License
See the repository's root `LICENSE`.
