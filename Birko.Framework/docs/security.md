# Security Guide

## Overview

Birko.Security provides password hashing, AES-256-GCM encryption, token provider interfaces, static token authentication, and RBAC interfaces. Birko.Security.Jwt adds JWT token generation and validation.

## Password Hashing

### IPasswordHasher

```csharp
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
```

### Pbkdf2PasswordHasher

PBKDF2-SHA512 with 600,000 iterations. Hash format is self-contained: `PBKDF2-SHA512:600000:salt:hash`.

```csharp
var hasher = new Pbkdf2PasswordHasher();

var hash = hasher.Hash("mypassword");       // "PBKDF2-SHA512:600000:base64salt:base64hash"
var valid = hasher.Verify("mypassword", hash);  // true
```

Uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.

## Encryption

### IEncryptionProvider

```csharp
public interface IEncryptionProvider
{
    byte[] Encrypt(byte[] plaintext, byte[] key);
    byte[] Decrypt(byte[] ciphertext, byte[] key);
    string EncryptString(string plaintext, byte[] key);
    string DecryptString(string ciphertext, byte[] key);
}
```

### AesEncryptionProvider

AES-256-GCM with embedded nonce and authentication tag:

```csharp
var provider = new AesEncryptionProvider();

// Generate a 256-bit key
var key = AesEncryptionProvider.GenerateKey();

// Encrypt/decrypt strings (Base64 output)
var encrypted = provider.EncryptString("sensitive data", key);
var decrypted = provider.DecryptString(encrypted, key);

// Encrypt/decrypt bytes
var encryptedBytes = provider.Encrypt(plainBytes, key);
var decryptedBytes = provider.Decrypt(encryptedBytes, key);
```

The ciphertext includes the nonce and GCM authentication tag — no separate IV storage needed.

## Token Provider

### ITokenProvider

```csharp
public interface ITokenProvider
{
    TokenResult GenerateToken(IDictionary<string, string> claims);
    TokenValidationResult ValidateToken(string token);
    string GenerateRefreshToken();
}
```

### JWT Implementation (Birko.Security.Jwt)

```csharp
var options = new TokenOptions
{
    Secret = "my-secret-key-at-least-32-chars-long!",
    Issuer = "myapp",
    Audience = "myapp-api",
    ExpirationMinutes = 60,
    RefreshExpirationDays = 7
};

var provider = new JwtTokenProvider(options);

// Generate token
var claims = new Dictionary<string, string>
{
    ["sub"] = userId.ToString(),
    ["role"] = "Admin"
};
var result = provider.GenerateToken(claims);
// result.Token = "eyJhbG..."
// result.ExpiresAt = DateTime.UtcNow + 60 minutes

// Validate token
var validation = provider.ValidateToken(result.Token);
if (validation.IsValid)
{
    var userClaims = validation.Claims;
}

// Generate opaque refresh token (256-bit random, stored in DB)
var refreshToken = provider.GenerateRefreshToken();
```

JWT tokens auto-include `jti` (unique ID) and `iat` (issued-at) claims. ClockSkew is 1 minute.

## Static Token Authentication

Moved from `Birko.Communication.Authentication` to `Birko.Security.Authentication`:

```csharp
var config = new AuthenticationConfiguration
{
    Enabled = true,
    Tokens = new[] { "api-token-1", "api-token-2" },
    TokenBindings = new[]
    {
        new TokenBinding { Token = "api-token-1", AllowedIPs = new[] { "192.168.1.0/24" } }
    }
};

var service = new AuthenticationService(config);
service.ValidateToken("api-token-1", "192.168.1.100");  // OK
service.ValidateToken("api-token-1", "10.0.0.1");       // Throws
```

## RBAC Interfaces

```csharp
public interface IRoleProvider
{
    Task<IEnumerable<string>> GetRolesAsync(Guid userId, CancellationToken ct = default);
}

public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(AuthorizationContext context, string permission,
                                   CancellationToken ct = default);
}
```

`AuthorizationContext` is a POCO carrying user claims for authorization decisions.

## See Also

- [Birko.Security CLAUDE.md](../Birko.Security/CLAUDE.md)
- [Birko.Security.Jwt CLAUDE.md](../Birko.Security.Jwt/CLAUDE.md)
