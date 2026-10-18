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

### FieldCipher

For a value stored in a database row (an API key, a token), `FieldCipher` (TASK-532) adds what a raw key lacks: the
key is derived with HKDF from a master secret, the envelope `bfc1:{keyId}:{payload}` names the key it was encrypted
under (so masters can rotate), and the caller's associated data is authenticated (so a value copied into another row
does not decrypt). Masters must be at least 32 random bytes; a missing one throws at construction.

```csharp
// Master secrets live in a secret store as base64 (FieldCipher.GenerateMasterSecret()), one per key id.
var cipher = await FieldCipher.FromSecretProviderAsync(secrets, currentKeyId: "2026-10", previousKeyIds: ["2026-01"]);
var stored = cipher.Encrypt(apiKey, associatedData: $"provider-config/{row.Id}/apiKey");  // bfc1:2026-10:...
var apiKey = cipher.Decrypt(stored, $"provider-config/{row.Id}/apiKey");
if (cipher.NeedsReencryption(stored)) stored = cipher.Reencrypt(stored, $"provider-config/{row.Id}/apiKey");
```

### InMemorySecretProvider

An `ISecretProvider` for local runs, tests and secrets that arrive through configuration, with no vault:
`new InMemorySecretProvider(configuration.GetSection("Secrets").AsEnumerable(makePathsRelative: true))`.

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
    var userClaims = validation.Claims;      // one value per type; a repeated type is joined with ';'
    var roles = validation.ClaimValues["role"]; // every value of a repeated type
}

// Generate opaque refresh token (256-bit random, stored in DB)
var refreshToken = provider.GenerateRefreshToken();
```

JWT tokens auto-include `jti` (unique ID) and `iat` (issued-at) claims. ClockSkew is 1 minute.

`ValidateToken` always checks the issuer and the audience, as `AddBirkoJwtBearer` does: options without an `Issuer` or
an `Audience` throw `ArgumentException` (a configuration error, like a missing `Secret`). It used to skip the check and
accept any token signed with the secret, whichever service minted it (SH-M257). A claim type the token carries more
than once (several roles, an `aud` array) validates: `Claims` joins its values with `;`, `ClaimValues` lists them
(SH-M248; it used to report the token invalid).

## Static Token Authentication

Moved from `Birko.Communication.Authentication` to `Birko.Security.Authentication`:

```csharp
var config = new RestAuthenticationConfiguration   // or the SOAP / SSE / WebSocket configuration
{
    Enabled = true,
    Tokens = { "api-token-2" },
    TokenBindings = { new TokenBinding { Token = "api-token-1", AllowedIps = { "192.168.1.100" } } },
    TrustedProxies = { "10.0.0.5", "10.1.0.0/16" }   // optional; empty trusts no proxy
};

var service = new AuthenticationService(config);
service.ValidateToken("api-token-1", "192.168.1.100");  // true
service.ValidateToken("api-token-1", "10.0.0.1");       // false: a bound token from another address
```

A binding's `AllowedIps` are exact addresses. The client address a transport checks them against is the connection's
own: `X-Forwarded-For`, `X-Real-IP` and `CF-Connecting-IP` are believed **only** when the connection comes from one of
`TrustedProxies` (an address or a CIDR range), and `X-Forwarded-For` is then read from the right, skipping trusted
proxies, so the client cannot choose the answer by writing the header itself. Before SH-M249 the headers were believed
from anyone, so a caller holding a bound token could name an allowed address and pass. **Behind a reverse proxy, list
it in `TrustedProxies`**, or every request is seen as coming from the proxy. A binding whose every address expands to
nothing refuses its token from everywhere and is logged at error level at construction (SH-M250).

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

### Loopback-only bypass (TASK-522)

`options.LoopbackBypass = true` (off by default) lets a token-less request through as a principal holding `*` in the
configured `PermissionClaim`, but only while every address the server actually bound is loopback (read per request from
`IServerAddressesFeature`, not from configured URLs). Unknown, wildcard (`0.0.0.0`, `[::]`, `*`, `+`) or other
addresses enforce authentication, and a request with a valid token keeps its own principal. It needs
`app.UseBirkoLoopbackBypass()` after `UseAuthentication()`; without it the host refuses to start. Never enable it behind
a reverse proxy on the same machine: the proxy forwards remote callers over loopback.

### ICurrentUser

Access the authenticated user from any service via DI:

```csharp
public class MyService(ICurrentUser currentUser)
{
    public void DoWork()
    {
        var userId = currentUser.UserGuid;
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

### Tenant/Claim Guard

A tenant resolver only *parses* its source — it does not compare the result to the JWT `tenant_id` claim, and
it cannot: `TenantMiddleware` runs **before** `UseAuthentication()`, so there is no claim to compare against
yet. Left uncorrelated the resolved tenant and the claim feed *different* consumers (repository tenant scoping
follows the resolution, permission resolution follows the token), so a caller can authenticate in their own
tenant, address another tenant, keep their own permissions and point every tenant-scoped read **and write**
at that tenant.

`UseBirkoTenantHeaderGuard()` closes this as a separate post-authentication step:

```csharp
app.UseMiddleware<TenantMiddleware>();   // resolves the tenant
app.UseAuthentication();
app.UseAuthorization();
app.UseBirkoTenantHeaderGuard();         // ← resolved tenant must agree with the claim
// …tenant-scoped middleware and endpoints
```

**The guard is on the resolved tenant, not on a transport.** It originally compared one hard-coded
`X-Tenant-Id` header, which left every other door open (SH-H048): a query-string key, a route value, a
subdomain, either custom-resolver hook, and — the quiet one — a **renamed**
`TenantMiddlewareOptions.TenantHeaderName`, which made the guard stop working silently on a deployment that
looked correctly configured. Each resolving middleware now publishes its result as a `ResolvedTenant` on
`HttpContext.Items`, and the guard checks that, so a source added later is covered without touching the guard:

| Source | Stack | Covered |
|---|---|---|
| `X-Tenant-Id` header | either | ✅ (also checked literally — see below) |
| `TenantMiddlewareOptions.TenantHeaderName`, renamed | `Birko.Data.Tenant` | ✅ |
| `TenantMiddlewareOptions.TenantQueryStringKey` | `Birko.Data.Tenant` | ✅ |
| `TenantMiddlewareOptions.TenantRouteKey` | `Birko.Data.Tenant` | ✅ |
| `TenantMiddlewareOptions.CustomTenantResolver` | `Birko.Data.Tenant` | ✅ |
| `SubdomainTenantResolver` | `Birko.Security.AspNetCore` | ✅ |
| any custom `ITenantResolver` | `Birko.Security.AspNetCore` | ✅ |

The literal `X-Tenant-Id` check is retained **on top of** the resolved-tenant check: an app that never wired a
tenant middleware resolves nothing, yet its own code may still read the header directly.

The resolution travels on `HttpContext.Items` rather than through `ITenantContext` on purpose.
`UseTenantMiddleware` captures its `ITenantContext` from `ApplicationServices` at startup while the guard
resolves one from `RequestServices`, so under `AddTenantContextScoped()` the two are different objects — a
guard reading the interface would observe no tenant and wave the request through. Silently, which is the
failure class being fixed.

Order matters in both directions: after `UseAuthentication()` because the claim only exists there, and before
anything that scopes by tenant so every downstream consumer sees the same validated tenant. A mismatch
short-circuits with `403` and `{"Error": "…", "Code": "Tenant.HeaderClaimMismatch"}`.

**Secure by default.** `BirkoSecurityOptions.RequireTenantHeaderMatchesClaim` defaults to `true`; an opt-*in*
guard was rejected as a design, since a check nobody knows to enable protects nobody. Set it false only for an
app that genuinely wants header-only tenancy — which re-opens the escalation above. With the flag off the
middleware is a pass-through, so the call is safe to make unconditionally.

```csharp
builder.Services.AddBirkoSecurity(options =>
{
    options.RequireTenantHeaderMatchesClaim = false; // header-only tenancy, no token correlation
});
```

These requests pass through unchecked, each deliberately:

| Case | Why it is safe |
|------|----------------|
| No tenant addressed | The claim is then the only tenant source — and server-sent events cannot set headers |
| Unauthenticated | Login / register / first-run setup carry no claim and are anonymous by design |
| Wildcard `*` holder | A superadmin's cross-tenant reach is intentional |
| Unparseable source | Resolves to no tenant anyway, so it cannot scope anything |

`Guid.Empty` (the system/no-tenant scope) is compared like any other value, so a non-wildcard caller whose
token was issued for it cannot address a real tenant.

### Token Service Adapter

Wraps `ITokenProvider` with structured request/response:

```csharp
var adapter = new TokenServiceAdapter(jwtProvider, options);

var token = adapter.GenerateAccessToken(new TokenRequest(
    UserGuid: userId, Email: "user@example.com",
    TenantId: tenantId, Roles: ["Admin"], Permissions: ["users.read"]));

var info = adapter.ValidateToken(token.Token);
// info.UserGuid, info.Email, info.TenantId, info.Roles, info.Permissions
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

`IsHierarchical` (default `true`) says how `ListSecretsAsync` answers: names relative to the path listed (Vault KV), or
full names starting with it (`false`: Azure Key Vault, whose names are flat). The configuration provider walks the two
differently (SH-M254).

A key is a path, never a URL fragment. Vault escapes each `/`-separated segment and refuses an empty, `.` or `..` one
(SH-M251: `../../sys/policy/x` used to reach another Vault endpoint, a delete included). Azure Key Vault refuses a name
that is not 1-127 letters, digits and `-` (SH-M252), and `ListSecretsAsync` reads every page of a large vault, following
`nextLink` only on the vault's own address (SH-M260).

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

On a flat provider (Azure Key Vault) the vault is listed once, and `--` in a name is a level: with path `app`, the
secret `app--db--password` loads as `db:password:value`, where Vault's `app/db/password` loads too.

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

### Federated sign-in (TASK-517)

A subject who signed in elsewhere (GitHub, a corporate IdP) gets the server's own token pair from
`TokenEndpointHandler.IssueForSubjectAsync(clientId, subject, scope, claims)`. The pair behaves as if an authorization had
just completed, so the refresh token renews through the normal `refresh_token` grant, with rotation and reuse detection.

```csharp
var tokens = await oauthServer.Token.IssueForSubjectAsync(
    "web", "github:42", "read write",
    new Dictionary<string, string> { ["name"] = "octocat", ["roles"] = "admin" });
```

- The client must exist, be enabled and be allowed `refresh_token`. The scope is narrowed as for `client_credentials`.
- Extra claims are stored on `RefreshTokenRecord.Claims` and re-issued on every refresh, so a store must persist that
  property. A registered claim (`sub`, `client_id`, `iss`, `scope`, `aud`, `exp`, `iat`, `nbf`, `jti`) is refused.
- No client secret is checked: the host asserts the sign-in. Never expose it as an endpoint.

### Shaping a client_credentials token (TASK-520)

`TokenEndpointHandler.ShapeClientCredentialsToken` sets a `client_credentials` token's subject (e.g. `service:billing`) and
extra claims after the client is authenticated and its scope granted, so a service account needs no replacement grant.
A registered claim or an empty subject is refused. The scope stays space-delimited; with `PermissionClaim = "scope"`,
Birko.Security.AspNetCore reads it as separate permissions (`SplitPermissionsOnSpace`, null = only for `scope`).

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

### ASP.NET Core endpoints (Birko.Security.OAuth.Server.AspNetCore)

`MapOAuthServer()` maps the handlers onto minimal-API routes (TASK-519, from DraCode's `OAuthEndpoints.cs`). `OAuthServer`
is resolved from DI per request.

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapOAuthServer(o =>
{
    o.AllowDynamicRegistration = true;                          // off by default
    o.ConfigureRegistration = b => b.RequireAuthorization("admin");
    o.AuthorizeResponseMode = OAuthAuthorizeResponseMode.Redirect; // default Json (an SPA drives the redirect)
    o.GrantHandlers[OAuthGrantTypes.ClientCredentials] = myIssuer; // optional: the app takes over one grant
});
```

- **Routes.** `POST /token` and `POST /device_authorization` authenticate the client in the request: form fields or
  HTTP Basic, form-decoded per RFC 6749 §2.3.1. `GET /authorize` and `POST /device/approve` always require an
  authenticated caller, and take the user from the principal (`sub`, else NameIdentifier), never from a request field.
  `POST /register` and `/register/{clientId}/disable` exist only with `AllowDynamicRegistration`; they always require
  authorization, plus the app's policy.
- **Errors.** `{error, error_description, error_uri}` with 400. `invalid_client` after HTTP Basic is 401 with
  `WWW-Authenticate: Basic`, `server_error` is 500 and `temporarily_unavailable` 503.
- **Client authentication.** A malformed Basic header is `invalid_client`. A body secret beside Basic, or a body
  `client_id` naming another client, is `invalid_request`: a client uses one authentication method (§2.3).
- **Caching.** Token, device-authorization and registration responses carry `Cache-Control: no-store` and
  `Pragma: no-cache` (§5.1).
- **`/authorize`.** An error is never redirected, since the error may be the `redirect_uri` itself.
- **Grant handlers.** A grant handler owns client authentication for its grant; the framework checks nothing on that
  path.

### Persistence

You supply implementations of five interfaces — any `IAsyncStore<T>` backend works:

- `IOAuthClientStore` — registered OAuth clients
- `IAuthorizationCodeStore` — one-shot authorization codes
- `IRefreshTokenStore` — refresh tokens (stored as SHA-256 hashes, never plaintext)
- `IDeviceCodeStore` — in-flight RFC 8628 device-code requests
- `IConsentStore` — prior user-consent records (used to skip the consent UI on repeat visits)

Two companions ship all five (TASK-518), each with a DI extension that registers one shared instance per store:

```csharp
services.AddOAuthServerInMemoryStores();                  // Birko.Security.OAuth.Server.InMemory — nothing persists
services.AddOAuthServerSqLiteStores("data/oauth.db");     // Birko.Security.OAuth.Server.SqLite — tables oauth_*, created on first use
```

Single use is the store's job. `IAuthorizationCodeStore.TryRedeemAsync` claims a code and `IRefreshTokenStore.TryRevokeAsync`
claims a refresh token on rotation; the token endpoint answers `invalid_grant` to the loser. A lost rotation only
fails that request (decided by the user, TASK-518): two tabs refreshing together are not a replay, and the winner's new
pair stays valid. A request that reads the token after its rotation committed sees it revoked, which is reuse, and
revokes the family; the server cannot tell that late tab from a replay, so an app should refresh from one place
(birko-web-shell's `createAuthSession` shares one refresh per tab). The shipped stores make both claims atomic: a conditional update on SQLite,
a lock in memory. The interfaces' default implementations are a read-check-write and only safe for one caller, so a
custom store should override them. A store must also persist `RefreshTokenRecord.Claims` (TASK-517); the SQLite store
keeps it, and `OAuthClient`'s collections, in JSON columns.

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

## Per-Resource Access Grants (Birko.Security.Grants)

Global roles and permission strings answer "may this user edit presentations?". `Birko.Security.Grants` answers
"may this user edit **this** presentation?": owners, editors and viewers of one resource, or of every resource of a
type, optionally bound to a tenant. Pure library: no ASP.NET dependency, persistence through `Birko.Data.Stores`.

```csharp
using Birko.Security.Grants;
using Birko.Security.Grants.Services;

var table = new GrantOptions()
    .DefineRole("owner", "read", "edit", "delete", "share")
    .DefineRole("editor", "read", "edit")
    .DefineRole("viewer", "read")
    .ResourceType("presentation", ResourceVisibility.Public)   // anyone may read: a denied write is 403
    .ResourceType("draft")                                      // Granted (default): no read grant means 404
    .Build();

IGrantService grants = new GrantService(grantStore, table, new SystemDateTimeProvider());

await grants.GrantOwnerOnCreateAsync(creator, "presentation", id);           // the creator owns it
await grants.GrantAsync(creator, colleague, "presentation", id, "editor");   // owner shares it
bool canEdit = await grants.CanAsync(colleague, "presentation", id, "edit"); // true
```

- **Undeclared names refuse.** A role, action or resource type you pass that is missing from the table throws
  `GrantConfigurationException`; it never quietly answers "no".
- **A stored grant whose role you retired grants nothing**, so it cannot break that user's checks. Run
  `StaleGrantDetector.FindAsync(store, table)` at start-up, or register `StaleGrantHealthCheck`
  (Birko.Health.Security.Grants, TASK-654), to list such grants and migrate or revoke them; a role renamed in
  configuration shows up there, not as lost access nobody noticed.
- **Granting:** an admin, or a holder of the share action whose own actions cover the role granted (and the role
  replaced). Otherwise `GrantRefusedException` with `Grant.NotAuthorized` or `Grant.Escalation`.
- **A resource never loses its last owner** (`Grant.LastOwner`). Writes on one resource are serialised in-process,
  and a recount after the write covers several processes: it is a compensation, not a transaction, so for a moment a
  concurrent revoke can see zero owners before the grant is put back.
- **Tenant:** every call takes the tenant explicitly. A tenant-bound grant applies only in its tenant; an untenanted
  grant applies everywhere.
- **Revocation is immediate:** nothing is cached.

- **Invitations** are offered to an email and redeemed at sign-in only when the provider marked that email verified (pass the provider's `email_verified`, never `UserLogin.IsVerified`). Redemption re-checks that the inviter may still grant the role, and only ever adds actions.
- **The first admin** comes from `GrantSignInOptions.BootstrapAdmins` as `provider:subject`, never from an email or "the first user". `IAdminGrantService.RevokeAdminAsync` removes an admin but never the last.

### ASP.NET Core (Birko.Security.Grants.AspNetCore)

```csharp
builder.Services.AddBirkoGrants(grants => grants
        .DefineRole("owner", "read", "edit", "delete", "share")
        .DefineRole("viewer", "read")
        .ResourceType("presentation", ResourceVisibility.Public),
    signIn => signIn.BootstrapAdmins.Add("github:4821337"))
    .WithGrantPermissions();   // admin grant → "*", type-wide grants → "{type}:{action}"

app.MapGet("/presentations/{id}", ...);                                   // public read
app.MapPut("/presentations/{id}", ...).RequireGrant("presentation", "edit");
```

- **Denials follow the visibility rule** (rulebook rule 65): **403** when the caller may read the resource, **404**
  when they may not, identical to a missing one. Not signed in is 401, decided before any lookup.
- **Your token's subject must be your user's Guid.** `RequireGrant` answers 401 `Grant.UnmappedSubject` to a token
  carrying the provider's raw subject.
- The end-to-end external-login wiring is in the project README.

### SQL backends (Birko.Security.Grants.SQL)

Schemaless stores need nothing. On SQL, register the mappings once, then ensure the tables and lookup indexes:

```csharp
GrantSqlMappings.Register(new ModelMapRegistry()).ApplyToDatabase();
await GrantSqlIndexes.EnsureAsync(grantStore, invitationStore, c => new SqLiteIndexManager(c));
```

`SqlResourceGrantStore<DB>` / `SqlInvitationStore<DB>` work on any provider. Pass that provider's index manager:
the base `SqlIndexManager` speaks only MySQL's `information_schema`.

## See Also

- [Birko.Security](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security)
- [Birko.Security.Jwt](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.Jwt)
- [Birko.Security.AspNetCore](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.AspNetCore)
- [Birko.Security.BCrypt](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.BCrypt)
- [Birko.Security.OAuth.Server](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.OAuth.Server)
- [Birko.Security.Grants](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.Grants)
- [Birko.Security.Vault](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.Vault)
- [Birko.Security.Vault.Configuration](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.Vault.Configuration)
- [Birko.Communication.OAuth](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Communication.OAuth) — client-side flows (companion to the server)
- [Birko.Security.AzureKeyVault](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.AzureKeyVault)
- [Birko.Security.NFC](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Security.NFC)
