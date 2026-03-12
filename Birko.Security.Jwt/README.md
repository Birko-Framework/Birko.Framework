# Birko.Security.Jwt

JWT implementation of ITokenProvider for the Birko Framework.

## Features

- JWT token generation and validation
- Opaque refresh token support
- Structured validation results
- Configurable token settings (expiration, signing key, issuer, audience)

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
