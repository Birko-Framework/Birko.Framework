# Birko.Security.AspNetCore

ASP.NET Core integration for Birko.Security — JWT Bearer authentication, current user resolution, permission checking, and multi-tenant middleware.

## Features

- One-line DI setup via `AddBirkoSecurity()`
- JWT Bearer authentication with configurable claim mapping
- `ICurrentUser` interface for accessing authenticated user from any service
- Claims-based permission checking with wildcard superadmin support
- Minimal API `RequirePermission()` endpoint filter
- Multi-tenant middleware with header, subdomain, and custom resolution strategies
- `TokenServiceAdapter` for structured token generation and validation

## Dependencies

- Birko.Security (IPermissionChecker, IPasswordHasher, IEncryptionProvider)
- Birko.Security.Jwt (JwtTokenProvider, TokenOptions, TokenResult)
- Birko.Data.Tenant (ITenantContext)
- Microsoft.AspNetCore.Authentication.JwtBearer

## Usage

### Setup

```csharp
builder.Services.AddBirkoSecurity(options =>
{
    options.JwtOptions.Secret = "my-secret-key-at-least-32-chars-long!";
    options.JwtOptions.Issuer = "myapp";
    options.JwtOptions.Audience = "myapp-api";
    options.TenantResolver = TenantResolverType.Header;
});

// Add tenant middleware
app.UseTenantMiddleware();
```

### ICurrentUser

```csharp
public class OrderService(ICurrentUser currentUser)
{
    public void CreateOrder()
    {
        var userId = currentUser.UserId;
        var tenantGuid = currentUser.TenantGuid;
        var roles = currentUser.Roles;
        var permissions = currentUser.Permissions;
    }
}
```

### Permission Endpoint Filters

```csharp
app.MapGet("/admin/users", () => { /* ... */ })
   .RequirePermission("users.read");

app.MapDelete("/admin/users/{id}", (Guid id) => { /* ... */ })
   .RequirePermission("users.delete");
```

### Tenant Resolution

Three built-in strategies:

| Strategy | Resolution |
|----------|-----------|
| **Header** | `X-Tenant-Id` and `X-Tenant-Name` HTTP headers |
| **Subdomain** | Hostname subdomain (e.g., `acme.myapp.com`) with optional async lookup |
| **Custom** | Provide your own `ITenantResolver` implementation |

### Token Service Adapter

```csharp
var adapter = new TokenServiceAdapter(jwtProvider, options);

var token = adapter.GenerateAccessToken(new TokenRequest(
    UserId: userId, Email: "user@example.com",
    TenantGuid: tenantGuid, Roles: ["Admin"], Permissions: ["users.read"]));

var info = adapter.ValidateToken(token.Token);
```

## Project Structure

```
Birko.Security.AspNetCore/
├── User/
│   ├── ICurrentUser.cs              - Authenticated user interface
│   ├── ClaimMappingOptions.cs       - JWT claim-to-property mapping
│   └── ClaimsCurrentUser.cs         - HttpContext-based ICurrentUser
├── Authentication/
│   ├── JwtClaimNames.cs             - Standard claim name constants
│   ├── JwtAuthenticationOptions.cs  - JWT Bearer configuration
│   ├── JwtBearerExtensions.cs       - AddBirkoJwtBearer() DI extension
│   └── TokenServiceAdapter.cs       - ITokenProvider wrapper with structured records
├── Authorization/
│   ├── ClaimsPermissionChecker.cs   - IPermissionChecker from JWT claims
│   └── PermissionEndpointFilter.cs  - Minimal API RequirePermission() filter
├── Tenant/
│   ├── ITenantResolver.cs           - Interface + TenantInfo record
│   ├── HeaderTenantResolver.cs      - Header-based tenant resolution
│   ├── SubdomainTenantResolver.cs   - Subdomain-based tenant resolution
│   ├── TenantContextAdapter.cs      - Birko.Data.Tenant adapter for scoped DI
│   └── TenantMiddleware.cs          - Request-scoped tenant middleware
└── Extensions/
    └── SecurityServiceExtensions.cs - AddBirkoSecurity() one-line DI
```

## Related Projects

- [Birko.Security](../Birko.Security/) - Core security interfaces and implementations
- [Birko.Security.Jwt](../Birko.Security.Jwt/) - JWT token provider
- [Birko.Data.Tenant](../Birko.Data.Tenant/) - Multi-tenancy support

## License

Part of the Birko Framework.
