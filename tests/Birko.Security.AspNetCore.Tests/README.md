# Birko.Security.AspNetCore.Tests

Unit tests for the Birko.Security.AspNetCore project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **.NET 10.0**

## Test Structure

| Directory | Tests For |
|-----------|-----------|
| Authentication/ | JwtClaimNames, JwtAuthenticationOptions, TokenServiceAdapter |
| Authorization/ | ClaimsPermissionChecker, PermissionEndpointFilter |
| Tenant/ | HeaderTenantResolver, SubdomainTenantResolver, TenantContextAdapter, TenantMiddleware |
| User/ | ClaimMappingOptions, ClaimsCurrentUser |
| Extensions/ | BirkoSecurityOptions |

## Running Tests

```bash
dotnet test Birko.Security.AspNetCore.Tests.csproj
```

## License

Part of the Birko Framework.
