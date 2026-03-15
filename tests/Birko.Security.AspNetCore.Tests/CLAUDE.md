# Birko.Security.AspNetCore.Tests

## Overview
Unit tests for the Birko.Security.AspNetCore project — JWT Bearer auth, ICurrentUser, permission checking, tenant resolution, and middleware.

## Location
`C:\Source\Birko.Security.AspNetCore.Tests\`

## Structure
```
Birko.Security.AspNetCore.Tests/
├── Authentication/
│   ├── JwtClaimNamesTests.cs              - Constant value verification
│   ├── JwtAuthenticationOptionsTests.cs   - Defaults, EffectiveAudience, property round-trips
│   └── TokenServiceAdapterTests.cs        - Generate/validate roundtrip with real JwtTokenProvider
├── Authorization/
│   ├── ClaimsPermissionCheckerTests.cs    - Permission matching, wildcard, user mismatch
│   └── PermissionEndpointFilterTests.cs   - 401/403/pass-through with DefaultEndpointFilterInvocationContext
├── Tenant/
│   ├── HeaderTenantResolverTests.cs       - Valid/missing/invalid X-Tenant-Id header
│   ├── SubdomainTenantResolverTests.cs    - Base domain extraction, first-segment, no subdomain
│   ├── TenantContextAdapterTests.cs       - Delegation to Birko.Data.Tenant ITenantContext
│   └── TenantMiddlewareTests.cs           - Set/clear lifecycle, next called, cleanup on exception
├── User/
│   ├── ClaimMappingOptionsTests.cs        - Default claim types
│   └── ClaimsCurrentUserTests.cs          - Claims extraction, unauthenticated, custom mapping
└── Extensions/
    └── BirkoSecurityOptionsTests.cs       - Default options, property round-trips
```

## Dependencies
- **Birko.Data.Core** (projitems)
- **Birko.Data.Stores** (projitems)
- **Birko.Data.Tenant** (projitems)
- **Birko.Security** (projitems)
- **Birko.Security.Jwt** (projitems)
- **Birko.Security.AspNetCore** (projitems)
- **Microsoft.AspNetCore.App** (FrameworkReference)
- **System.IdentityModel.Tokens.Jwt** 8.4.0
- **xUnit** 2.9.3, **FluentAssertions** 7.0.0

## Test Patterns
- Inline test stubs (TestCurrentUser, TestTenantResolver, TestTenantContext, TestHttpContextAccessor)
- DefaultHttpContext for HTTP pipeline tests
- Real JwtTokenProvider for token roundtrip tests (no mocks)
- Three-part naming: `Method_Scenario_Expected`

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed test files and patterns.

### Test Requirements
When adding new features to Birko.Security.AspNetCore:
- Add corresponding test classes in the matching directory
- Follow existing patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
