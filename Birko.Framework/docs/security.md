# Security Guide

## Overview

Birko.Security provides password hashing, AES-256-GCM encryption, token provider interfaces, static token authentication, and RBAC interfaces. Birko.Security.Jwt adds JWT token generation and validation. Birko.Security.AspNetCore provides ASP.NET Core integration — JWT Bearer authentication, current user resolution, permission checking, and multi-tenant middleware.

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

### BCryptPasswordHasher (Birko.Security.BCrypt)

Pure C# BCrypt implementation with configurable work factor (4–31, default 12). Output format: standard `$2a$XX$` modular crypt. No external NuGet dependencies.

```csharp
using Birko.Security.BCrypt.Hashing;

var hasher = new BCryptPasswordHasher();              // default work factor 12
var hasher5 = new BCryptPasswordHasher(workFactor: 5); // faster, less secure

var hash = hasher.Hash("mypassword");       // "$2a$12$..."
var valid = hasher.Verify("mypassword", hash);  // true

// Check if stored hash needs upgrade to current work factor
if (hasher.NeedsRehash(oldHash))
{
    var newHash = hasher.Hash(password);
}
```

BCrypt is more GPU-resistant than PBKDF2 due to its memory-bound Blowfish state (~4KB). Use BCrypt when stronger protection against hardware-accelerated brute force is needed. The 72-byte password limit (UTF-8, null-terminated) is per the BCrypt specification.

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

## ASP.NET Core Integration (Birko.Security.AspNetCore)

Bridges Birko.Security into ASP.NET Core applications with a single DI call.

### One-Line Setup

```csharp
builder.Services.AddBirkoSecurity(options =>
{
    options.JwtOptions.Secret = "my-secret-key-at-least-32-chars-long!";
    options.JwtOptions.Issuer = "myapp";
    options.JwtOptions.Audience = "myapp-api";
    options.TenantResolver = TenantResolverType.Header; // or Subdomain, Custom
});
```

This registers: JWT Bearer authentication, `ICurrentUser`, `IPermissionChecker`, `ITenantResolver`, and `ITenantContext`.

### ICurrentUser

Access the authenticated user from any service via DI:

```csharp
public class MyService(ICurrentUser currentUser)
{
    public void DoWork()
    {
        var userId = currentUser.UserId;
        var email = currentUser.Email;
        var tenantId = currentUser.TenantId;
        var roles = currentUser.Roles;
        var permissions = currentUser.Permissions;
    }
}
```

`ClaimsCurrentUser` reads claims from `HttpContext` automatically.

### Permission-Based Authorization

#### Claims Permission Checker

`ClaimsPermissionChecker` implements `IPermissionChecker` by reading permissions from JWT claims. Supports wildcard `"*"` for superadmin access.

#### Minimal API Endpoint Filters

```csharp
app.MapGet("/admin/users", () => { /* ... */ })
   .RequirePermission("users.read");

app.MapDelete("/admin/users/{id}", (Guid id) => { /* ... */ })
   .RequirePermission("users.delete");
```

### Tenant Resolution

Three built-in strategies:

| Strategy | How it resolves |
|----------|----------------|
| **Header** | `X-Tenant-Id` and `X-Tenant-Name` HTTP headers |
| **Subdomain** | Hostname subdomain (e.g., `acme.myapp.com` → `acme`) with optional async lookup |
| **Custom** | Provide your own `ITenantResolver` implementation |

`TenantMiddleware` runs per-request to resolve the tenant and populate `ITenantContext` (scoped).

### Token Service Adapter

Wraps `ITokenProvider` with structured request/response:

```csharp
var adapter = new TokenServiceAdapter(jwtProvider, options);

var token = adapter.GenerateAccessToken(new TokenRequest(
    UserId: userId, Email: "user@example.com",
    TenantId: tenantId, Roles: ["Admin"], Permissions: ["users.read"]));

var info = adapter.ValidateToken(token.Token);
// info.UserId, info.Email, info.TenantId, info.Roles, info.Permissions
```

## Secret Management

### ISecretProvider

```csharp
public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default);
    Task SetSecretAsync(string key, string value, CancellationToken ct = default);
    Task DeleteSecretAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListSecretsAsync(string? path = null, CancellationToken ct = default);
    async Task<IReadOnlyDictionary<string, string>?> GetSecretPairsAsync(string key, CancellationToken ct = default) { ... }
}
```

`SecretResult` includes `Key`, `Value`, `CreatedAt`, `UpdatedAt`, `ExpiresAt`, `Version`, and `Metadata`.

`GetSecretPairsAsync` returns all key/value pairs at a given path (multi-field secrets in Vault KV). Flat providers return `{"value": <secret>}` by default.

### HashiCorp Vault (Birko.Security.Vault)

Uses Vault HTTP API directly — no VaultSharp dependency. Supports KV v1 and v2. `VaultSettings` extends `PasswordSettings` (Location=Address, Password=Token, Name=MountPath).

```csharp
using Birko.Security.Vault;

var settings = new VaultSettings("https://vault.example.com:8200", "hvs.token", "secret");
settings.KvVersion = 2;

using var vault = new VaultSecretProvider(settings);
await vault.SetSecretAsync("myapp/db-pass", "s3cret");
var password = await vault.GetSecretAsync("myapp/db-pass");
var healthy = await vault.IsHealthyAsync();
```

### Azure Key Vault (Birko.Security.AzureKeyVault)

Uses Key Vault REST API with OAuth2 client credentials — no Azure SDK dependency. `AzureKeyVaultSettings` extends `RemoteSettings` (Location=VaultUri, UserName=ClientId, Password=ClientSecret, Name=TenantId).

```csharp
using Birko.Security.AzureKeyVault;

var settings = new AzureKeyVaultSettings("https://myvault.vault.azure.net/", "tenant-id", "client-id", "client-secret");

using var akv = new AzureKeyVaultSecretProvider(settings);
await akv.SetSecretAsync("db-password", "s3cret");
var password = await akv.GetSecretAsync("db-password");
```

### Configuration Integration (Birko.Security.Vault.Configuration)

Plugs any `ISecretProvider` into `Microsoft.Extensions.Configuration`. Works with Vault, Azure Key Vault, and any future provider.

```csharp
using Birko.Security.Configuration;

// Provider-agnostic — works with any ISecretProvider
config.AddSecretConfiguration(vaultProvider, "myapp/db", recursive: true);
config.AddSecretConfiguration(azureKeyVaultProvider, "ConnectionStrings", recursive: false);

// Multiple paths (later overrides earlier)
config.AddSecretConfiguration(provider, new[] { "defaults", "production" });
```

Vault-specific convenience method with hierarchical path convention:

```csharp
using Birko.Security.Vault.Configuration;

// Reads LOCAL_VAULT_* env vars, builds path hierarchy: defaults → env → project → user
config.AddLocalVaultConfiguration("myapp");
```

### NFC Authentication

Birko.Security.NFC maps NFC tag UIDs to user identities and optionally issues JWT tokens.

```csharp
using Birko.Security.NFC;

var store = new InMemoryNfcTagMappingStore();
var auth = new NfcAuthProvider(store, tokenProvider: jwtProvider, tokenOptions: tokenOptions);

// Enroll a card
await auth.EnrollAsync(userId, "04A1B2C3", "Office badge", "John Doe", "john@example.com");

// Authenticate on tap
var result = await auth.AuthenticateAsync("04A1B2C3");
if (result.IsAuthenticated)
{
    Console.WriteLine($"Welcome {result.UserName}, JWT: {result.Token?.Token}");
}

// Manage cards
await auth.RevokeAsync("04A1B2C3");
var tags = await auth.GetUserTagsAsync(userId);
```

Features:
- UID normalization (case-insensitive, strips separators)
- Configurable max tags per user, tag expiration, usage tracking
- `INfcTagMappingStore` persistence interface (implement with any Birko.Data store)
- `InMemoryNfcTagMappingStore` included for testing

## OAuth2 Authorization Server (Birko.Security.OAuth.Server)

`Birko.Security.OAuth.Server` is the issuer-side counterpart to `Birko.Communication.OAuth` — your service can mint access and refresh tokens for first-party apps or third-party clients. Pure handler library (no ASP.NET dependency); persistence flows through `Birko.Data.Stores` so the same code runs against SQL, ElasticSearch, MongoDB, etc.

### Supported grant types

| Grant | Constant | When to use |
|---|---|---|
| `client_credentials` | `OAuthGrantTypes.ClientCredentials` | Machine-to-machine — confidential clients only. No refresh token (RFC 6749 §4.4.3). |
| `authorization_code` | `OAuthGrantTypes.AuthorizationCode` | Server-side web apps. PKCE is enforced for public clients by default. |
| `refresh_token` | `OAuthGrantTypes.RefreshToken` | Rotation enabled by default — every refresh revokes the old token (RFC 6819 §5.2.2.3). |
| `urn:ietf:params:oauth:grant-type:device_code` | `OAuthGrantTypes.DeviceCode` | RFC 8628 — input-constrained devices (CLI, IoT, smart TV). Poll-interval enforced as `slow_down`. |

`password` (resource-owner) and implicit (`response_type=token`) are intentionally **not** supported — both are deprecated by OAuth 2.1.

### Composition root

`OAuthServer` owns one handler per endpoint. The host instantiates one (typically as a singleton) and routes incoming HTTP requests to the matching handler.

```csharp
using Birko.Security;
using Birko.Security.Jwt;
using Birko.Security.OAuth.Server;
using Birko.Security.OAuth.Server.Endpoints.Token;

var settings = new OAuthServerSettings
{
    Location = "https://auth.example.com/",
    Issuer = "https://auth.example.com/",
    AccessTokenLifetimeSeconds = 3600,
    SupportedScopes = { "read", "write", "admin" }
};

var tokenOptions = new TokenOptions
{
    Secret = builder.Configuration["Oauth:SigningSecret"]!,
    Issuer = settings.Issuer,
    Audience = "https://api.example.com/"
};

var server = new OAuthServer(
    settings,
    tokens: new JwtTokenProvider(tokenOptions),
    tokenOptions: tokenOptions,
    clientStore: clientStore,
    codeStore: codeStore,
    refreshStore: refreshStore,
    deviceStore: deviceStore,
    consentStore: consentStore,
    deviceVerificationUri: "https://auth.example.com/device");
```

### Wiring the /token endpoint

```csharp
app.MapPost("/token", async (HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    var request = new TokenRequest
    {
        GrantType = form["grant_type"]!,
        ClientId = form["client_id"]!,
        ClientSecret = form["client_secret"],
        Code = form["code"],
        RedirectUri = form["redirect_uri"],
        CodeVerifier = form["code_verifier"],
        RefreshToken = form["refresh_token"],
        DeviceCode = form["device_code"],
        Scope = form["scope"]
    };
    try
    {
        var response = await server.Token.HandleAsync(request);
        return Results.Json(new
        {
            access_token = response.AccessToken,
            token_type = response.TokenType,
            expires_in = response.ExpiresIn,
            refresh_token = response.RefreshToken,
            scope = response.Scope
        });
    }
    catch (OAuthServerException ex)
    {
        return Results.Json(TokenErrorResponse.From(ex), statusCode: 400);
    }
});
```

### Persistence

You supply implementations of five interfaces — any `IAsyncStore<T>` backend works:

- `IOAuthClientStore` — registered OAuth clients
- `IAuthorizationCodeStore` — one-shot authorization codes
- `IRefreshTokenStore` — refresh tokens (stored as SHA-256 hashes, never plaintext)
- `IDeviceCodeStore` — in-flight RFC 8628 device-code requests
- `IConsentStore` — prior user-consent records (used to skip the consent UI on repeat visits)

Tokens use whatever `ITokenProvider` you supply — typically `JwtTokenProvider` from `Birko.Security.Jwt`.

### Dynamic client registration

`ClientRegistrationHandler` implements RFC 7591:

```csharp
var registration = await server.ClientRegistration.RegisterAsync(new ClientRegistrationRequest
{
    Name = "My App",
    ClientType = OAuthClientType.Confidential,
    RedirectUris = { "https://app.example.com/callback" },
    AllowedGrantTypes = { OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken },
    AllowedScopes = { "read", "write" }
});

// registration.ClientSecret is returned ONCE here — never retrievable again.
```

The plaintext secret is shown only at registration; subsequent `GetAsync` calls omit it. Hosts should gate this endpoint behind their own admin-only authorization policy.

### What's out of scope

- **OpenID Connect** — `id_token`, UserInfo endpoint, discovery document. A separate `Birko.Security.OIDC.Server` is planned.
- **SAML 2.0** — different protocol entirely.

## See Also

- [Birko.Security](https://github.com/birko/Birko.Security)
- [Birko.Security.Jwt](https://github.com/birko/Birko.Security.Jwt)
- [Birko.Security.AspNetCore](https://github.com/birko/Birko.Security.AspNetCore)
- [Birko.Security.BCrypt](https://github.com/birko/Birko.Security.BCrypt)
- [Birko.Security.OAuth.Server](https://github.com/birko/Birko.Security.OAuth.Server)
- [Birko.Security.Vault](https://github.com/birko/Birko.Security.Vault)
- [Birko.Security.Vault.Configuration](https://github.com/birko/Birko.Security.Vault.Configuration)
- [Birko.Communication.OAuth](https://github.com/birko/Birko.Communication.OAuth) — client-side flows (companion to the server)
- [Birko.Security.AzureKeyVault](https://github.com/birko/Birko.Security.AzureKeyVault)
- [Birko.Security.NFC](https://github.com/birko/Birko.Security.NFC)
