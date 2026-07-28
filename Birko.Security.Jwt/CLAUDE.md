# Birko.Security.Jwt

## Overview
Two capabilities, both JWT:

1. **Outbound** — JWT implementation of `ITokenProvider` from Birko.Security (we mint our own tokens).
2. **Inbound OIDC** (`OpenIdConnect/`) — verify an id token a *third-party* provider issued
   ("sign in with Google/Microsoft/Apple"). This is the piece the framework was missing:
   `Birko.Communication.OAuth` is an OAuth **client** (it obtains tokens for us to send elsewhere and
   carries an `IdToken` it never checks) and `Birko.Security.OAuth.Server` is an authorization
   **server** (it mints our tokens) — neither answers *"did this third party really vouch for this
   user?"*.

## Structure
```
Birko.Security.Jwt/
├── JwtTokenProvider.cs              - GenerateToken (JWT), GenerateRefreshToken (opaque), ValidateToken
└── OpenIdConnect/
    ├── OidcProviderOptions.cs       - per-provider ClientId / Issuer / JwksUri (+ FirstMissingSetting)
    ├── OidcVerificationResult.cs    - VerifiedOidcIdentity, OidcVerificationOutcome, result type
    ├── IOidcSigningKeySource.cs     - seam: where the provider's public keys come from
    ├── HttpOidcSigningKeySource.cs  - JWKS fetch + per-provider cache + rotation refresh
    ├── IOidcIdTokenVerifier.cs
    └── OidcIdTokenVerifier.cs       - signature/iss/aud/exp + asymmetric-only algorithm allow-list
```

## Dependencies
- **Birko.Security** (imports projitems — provides ITokenProvider, TokenResult, TokenOptions)
- **System.IdentityModel.Tokens.Jwt** / **Microsoft.AspNetCore.Authentication.JwtBearer** NuGet — added by consuming project
- `OpenIdConnect/` adds **no** new package: it uses `Microsoft.IdentityModel.*` (already required above)
  and `System.Net.Http` from the BCL. It deliberately takes a plain `HttpClient` rather than
  `IHttpClientFactory`, and **no** `IConfiguration` / `ILogger` / DI — so a console or worker consumer
  can use it too.

## Inbound OIDC — the rule that matters

A provider subject identifier (Google `sub`, GitHub id, Microsoft `oid`) is a stable **public** value:
it shows up in profile APIs, exports and logs. **Never resolve an account from a caller-supplied
provider key** — take it from `VerifiedOidcIdentity.Subject` and nowhere else. A login endpoint that
trusts the request is a *pre-authentication account takeover*: know the key, become the user. (Found and
fixed in a consumer — Symbio TASK-274, where an anonymous `POST /api/auth/oauth` returned a valid access
token for any subject and auto-provisioned a user + tenant to serve it. The mechanics were lifted here so
no consumer re-derives them.)

Two invariants the verifier owns:
- **Fail-closed config.** An unknown *or incompletely configured* provider is `ProviderNotConfigured` —
  a refusal, never "skip verification". `SigningKeysUnavailable` is reported separately from
  `TokenInvalid` so "we could not verify" is never mistaken for "the token is bad".
- **Asymmetric algorithms only.** Load-bearing, not hygiene: the provider's signing key is public, so if
  `HS256` were accepted an attacker could re-sign their own token using that public key as the HMAC
  secret and the same key material would verify it (algorithm confusion). `none` likewise excluded.

```csharp
var providers = new Dictionary<string, OidcProviderOptions>(StringComparer.OrdinalIgnoreCase)
{
    ["google"] = new()
    {
        ClientId = "…apps.googleusercontent.com",   // must equal the token's `aud`
        Issuer   = "https://accounts.google.com",   // must equal `iss`
        JwksUri  = "https://www.googleapis.com/oauth2/v3/certs",
    },
};

var verifier = new OidcIdTokenVerifier(new HttpOidcSigningKeySource(httpClient), providers);

var result = await verifier.VerifyAsync(provider, idToken, ct);
if (!result.IsVerified)
{
    // result.Outcome → your own error code; result.Reason is log-safe (never token/key material).
    log.LogWarning("refused {Provider}: {Outcome} — {Reason}", provider, result.Outcome, result.Reason);
    return Refuse();
}
// Only these values may identify the user. There is no fallback to anything the caller sent.
var (canonicalProvider, subject, email, emailVerified, displayName) = result.Identity!;
```

Guarded by `Framework.Tests/Birko.Security.Jwt.Tests/OidcIdTokenVerifierTests.cs` (36 tests incl. the
algorithm-confusion, wrong-audience, half-configured-provider and bare-subject cases).

**Note on `TokenValidationResult`:** `Birko.Security` defines one too, and it wins inside the
`Birko.Security.*` namespace hierarchy — `OidcIdTokenVerifier` aliases the IdentityModel type explicitly.

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
