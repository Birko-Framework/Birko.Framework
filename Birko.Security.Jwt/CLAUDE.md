# Birko.Security.Jwt

## Overview
JWT implementation of `ITokenProvider` from Birko.Security.

## Structure
```
Birko.Security.Jwt/
└── JwtTokenProvider.cs    - GenerateToken (JWT), GenerateRefreshToken (opaque), ValidateToken
```

## Dependencies
- **Birko.Security** (imports projitems — provides ITokenProvider, TokenResult, TokenOptions)
- **System.IdentityModel.Tokens.Jwt** / **Microsoft.AspNetCore.Authentication.JwtBearer** NuGet — added by consuming project

## Usage
```csharp
var options = new TokenOptions
{
    Secret = "my-secret-key-at-least-32-chars-long!",
    Issuer = "symbio",
    Audience = "symbio-api",
    ExpirationMinutes = 60,
    RefreshExpirationDays = 7
};
var provider = new JwtTokenProvider(options);

// Generate
var claims = new Dictionary<string, string> { ["sub"] = userId.ToString(), ["role"] = "Admin" };
var result = provider.GenerateToken(claims);
// result.Token = "eyJ..." , result.ExpiresAt = DateTime.UtcNow + 60min

// Validate
var validation = provider.ValidateToken(result.Token);
if (validation.IsValid) { /* use validation.Claims */ }

// Refresh token (opaque, not JWT)
var refreshToken = provider.GenerateRefreshToken(); // random base64 string
```

## Key Design Decisions
- JwtTokenProvider requires TokenOptions with Secret in constructor (fail-fast)
- Auto-adds `jti` (unique token ID) and `iat` (issued at) claims if not provided
- GenerateRefreshToken returns 256-bit random base64 — NOT a JWT (stored in DB, compared on refresh)
- ValidateToken returns structured TokenValidationResult with Claims dictionary or Error string
- ClockSkew set to 1 minute (default is 5 — too lenient)

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
