# Birko.Security.AspNetCore

ASP.NET Core integration for Birko.Security — JWT Bearer authentication, current user resolution, permission checking, and multi-tenant middleware.

## Features

- One-line DI setup via `AddBirkoSecurity()`
- JWT Bearer authentication with configurable claim mapping
- `ICurrentUser` interface for accessing authenticated user from any service
- Claims-based permission checking with wildcard superadmin support
- Minimal API `RequirePermission()` endpoint filter
- Multi-tenant middleware with header, subdomain, and custom resolution strategies
- Tenant header/claim guard — rejects an `X-Tenant-Id` that disagrees with the JWT tenant claim (on by default)
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

### Tenant Header/Claim Guard

`X-Tenant-Id` is only *parsed* by `HeaderTenantResolver` — it is not compared to the JWT `tenant_id` claim,
and it cannot be, because `TenantMiddleware` runs before authentication. Without a correlation step a caller
can authenticate in their own tenant, send another tenant's id in the header, keep their own permissions and
point tenant-scoped reads **and writes** at that tenant. `UseBirkoTenantHeaderGuard()` closes this after
authentication:

```csharp
app.UseMiddleware<TenantMiddleware>();   // resolves the header
app.UseAuthentication();
app.UseAuthorization();
app.UseBirkoTenantHeaderGuard();         // ← header must agree with the claim
```

A mismatch returns `403` with `{"Error": "…", "Code": "Tenant.HeaderClaimMismatch"}`.

On by default (`BirkoSecurityOptions.RequireTenantHeaderMatchesClaim = true`); set it false only for an app
that genuinely wants header-only tenancy. With the flag off the middleware is a pass-through, so the call is
safe to make unconditionally.

```csharp
builder.Services.AddBirkoSecurity(options =>
{
    options.RequireTenantHeaderMatchesClaim = false; // header-only tenancy, no token correlation
});
```

Requests pass through unchecked when there is no header (the claim is then the only tenant source — server-sent
events cannot set headers), when the caller is unauthenticated (login/register/setup), when the caller holds the
wildcard `*` permission (cross-tenant reach is intentional), or when the header is unparseable (it resolves to no
tenant anyway).

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
│   ├── TenantMiddleware.cs          - Request-scoped tenant middleware
│   └── TenantHeaderClaimGuardMiddleware.cs - 403 when X-Tenant-Id disagrees with the JWT claim
└── Extensions/
    ├── SecurityServiceExtensions.cs - AddBirkoSecurity() one-line DI
    └── TenantHeaderGuardExtensions.cs - UseBirkoTenantHeaderGuard() wiring
```

## Related Projects

- [Birko.Security](../Birko.Security/) - Core security interfaces and implementations
- [Birko.Security.Jwt](../Birko.Security.Jwt/) - JWT token provider
- [Birko.Data.Tenant](../Birko.Data.Tenant/) - Multi-tenancy support

## License

Part of the Birko Framework.
