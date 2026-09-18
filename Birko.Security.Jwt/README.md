# Birko.Security.Jwt

JWT token issuance and inbound OpenID Connect id-token verification for the Birko Framework.

## Features

- JWT token generation and validation
- Opaque refresh token support
- Structured validation results
- Configurable token settings (expiration, signing key, issuer, audience)
- **Inbound OIDC verification** (`OpenIdConnect/`) — verify a third-party provider's id token before
  trusting any identity it carries: JWKS signature, `iss`, `aud` (must be your client id), lifetime,
  an asymmetric-only algorithm allow-list, and the `sub` claim. Fail-closed on unknown or incompletely
  configured providers. Adds no new package dependency.

> **Security rule.** A provider subject identifier (Google `sub`, GitHub id) is a *public* value, not a
> secret. Never resolve an account from a provider key the caller sent — use
> `VerifiedOidcIdentity.Subject` from a verified token. Trusting the request is a pre-authentication
> account takeover.

## Installation

```bash
dotnet add package Birko.Security.Jwt
```

## Dependencies

- Birko.Security
- System.IdentityModel.Tokens.Jwt

## Usage

```csharp
using Birko.Security.Jwt;

var provider = new JwtTokenProvider(new JwtSettings
{
    SigningKey = "your-256-bit-secret-key",
    Issuer = "myapp",
    Audience = "myapp-users",
    TokenExpiration = TimeSpan.FromHours(1)
});

var token = await provider.GenerateTokenAsync(claims);
var refreshToken = provider.GenerateRefreshToken();
var result = await provider.ValidateTokenAsync(token);
```

## API Reference

- **JwtTokenProvider** - JWT ITokenProvider implementation
- **JwtSettings** - SigningKey, Issuer, Audience, TokenExpiration

## Related Projects

- [Birko.Security](../Birko.Security/) - Core security interfaces

## License

Part of the Birko Framework.
