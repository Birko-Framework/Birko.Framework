# Birko.Security.AspNetCore

## Overview
ASP.NET Core integration for Birko.Security — JWT Bearer authentication, current user resolution, claims-based permission checking, and multi-tenant middleware.

## Location
`C:\Source\Birko.Security.AspNetCore\`

## Structure
```
Birko.Security.AspNetCore/
├── User/
│   ├── ICurrentUser.cs              - Authenticated user interface (UserId, Email, TenantGuid, Roles, Permissions, IsAuthenticated, GetClaim)
│   ├── ClaimMappingOptions.cs       - Configuration for mapping JWT claim types to ICurrentUser properties
│   └── ClaimsCurrentUser.cs         - Resolves ICurrentUser from HttpContext JWT claims
├── Authentication/
│   ├── JwtClaimNames.cs             - Standard claim name constants (sub, email, tenant_id, role, permission)
│   ├── JwtAuthenticationOptions.cs  - JWT Bearer configuration (Secret, Issuer, Audience, Expiration, ClockSkew, Claims mapping)
│   ├── JwtBearerExtensions.cs       - AddBirkoJwtBearer() DI extension method
│   └── TokenServiceAdapter.cs       - Wraps ITokenProvider with TokenRequest/TokenValidationInfo records
├── Authorization/
│   ├── ClaimsPermissionChecker.cs   - IPermissionChecker from JWT claims (wildcard "*" superadmin support)
│   └── PermissionEndpointFilter.cs  - Minimal API RequirePermission() endpoint filter
├── Tenant/
│   ├── ITenantResolver.cs           - Interface + TenantInfo record for resolving tenant from HTTP request
│   ├── HeaderTenantResolver.cs      - X-Tenant-Id / X-Tenant-Name header resolution
│   ├── SubdomainTenantResolver.cs   - Subdomain-based tenant with optional async lookup
│   ├── TenantContextAdapter.cs      - Adapts Birko.Data.Tenant.Models.ITenantContext for scoped DI
│   └── TenantMiddleware.cs          - Request-scoped tenant resolution middleware
└── Extensions/
    └── SecurityServiceExtensions.cs - AddBirkoSecurity() one-line DI (JWT + User + Permissions + Tenant)
```

## Dependencies
- **Birko.Security** (imports projitems — provides IPermissionChecker, ITokenProvider)
- **Birko.Security.Jwt** (imports projitems — provides JwtTokenProvider, TokenOptions, TokenResult)
- **Birko.Data.Tenant** (imports projitems — provides ITenantContext)
- **Microsoft.AspNetCore.Authentication.JwtBearer** NuGet — added by consuming project

## Key Design Decisions
- `AddBirkoSecurity()` is the single entry point — registers JWT Bearer auth, ICurrentUser, IPermissionChecker, ITenantResolver, and ITenantContext in one call
- `BirkoSecurityOptions` configures JWT settings, tenant resolution strategy (Header/Subdomain/Custom), and wildcard permission flag
- `ClaimsCurrentUser` resolves lazily from `IHttpContextAccessor` — safe for scoped DI
- `ClaimsPermissionChecker` supports wildcard `"*"` permission for superadmin bypass
- `PermissionEndpointFilter` works with Minimal API endpoint filters (not MVC attributes)
- `TenantMiddleware` resolves tenant per-request and clears context after response
- `TokenServiceAdapter` wraps raw `ITokenProvider` with structured `TokenRequest`/`TokenValidationInfo` records for type-safe claim handling

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
