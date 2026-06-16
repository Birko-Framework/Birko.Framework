# Birko.Security.OAuth.Server

## Overview
OAuth2 authorization server implementation. Pure handler library — no ASP.NET dependency. Hosts route HTTP requests to per-endpoint handler classes; persistence flows through `Birko.Data.Stores` so the same code runs against SQL, ElasticSearch, MongoDB, etc.

Companion to `Birko.Communication.OAuth` (client side). Both projects can be used in the same process — a service issuing tokens with this project and consuming tokens from upstream providers with the client.

## Project Location
`C:\Source\Birko.Security.OAuth.Server\`

## Structure
```
Birko.Security.OAuth.Server/
├── OAuthServer.cs                  — Composition root: owns one handler per endpoint
├── OAuthServerSettings.cs          — Lifetime + issuer config (extends Birko.Configuration.Settings)
├── OAuthErrorCodes.cs              — RFC 6749 §5.2 + RFC 8628 error codes
├── OAuthGrantTypes.cs              — grant_type / response_type constants, OAuthClientType enum
├── OAuthServerException.cs         — { ErrorCode, ErrorDescription, ErrorUri } envelope
├── Models/                         — AbstractModel descendants (persistable via Birko.Data.Stores)
│   ├── OAuthClient.cs              — registered client
│   ├── AuthorizationCode.cs        — one-shot code with PKCE challenge
│   ├── RefreshTokenRecord.cs       — hashed refresh token + revocation
│   ├── DeviceCodeRecord.cs         — RFC 8628 in-flight state
│   └── ConsentRecord.cs            — prior user consent per (client, scope)
├── Stores/                         — IAsyncStore<T> subinterfaces with named lookups
│   ├── IOAuthClientStore.cs        — GetByClientIdAsync
│   ├── IAuthorizationCodeStore.cs  — GetByCodeAsync
│   ├── IRefreshTokenStore.cs       — GetByHashAsync
│   ├── IDeviceCodeStore.cs         — GetByDeviceCodeAsync / GetByUserCodeAsync
│   └── IConsentStore.cs            — GetAsync(userId, clientId)
├── Endpoints/
│   ├── Token/                      — /token (all four grant types)
│   ├── Authorize/                  — /authorize (response_type=code only; OAuth 2.1 default)
│   ├── DeviceAuthorization/        — /device_authorization + Approve helper
│   └── ClientRegistration/         — RFC 7591 dynamic client registration
└── Internal/
    ├── PkceValidator.cs            — S256 + plain (fixed-time compare)
    ├── ClientSecretHasher.cs       — SHA-256 hex, fixed-time verify
    └── RandomStringGenerator.cs    — Base64Url + RFC 8628 user-code alphabet
```

## Dependencies
- `Birko.Configuration` — Settings hierarchy
- `Birko.Data.Stores` — IAsyncStore<T>
- `Birko.Data.Core` — AbstractModel
- `Birko.Security` — ITokenProvider, TokenOptions (concrete token impl supplied by consumer, e.g. JwtTokenProvider from Birko.Security.Jwt)
- `Birko.Time.Abstractions` — IDateTimeProvider (testability)

## Grant types supported
| Grant | Constant | Notes |
|---|---|---|
| `client_credentials` | `OAuthGrantTypes.ClientCredentials` | Confidential clients only. No refresh per RFC 6749 §4.4.3. |
| `authorization_code` | `OAuthGrantTypes.AuthorizationCode` | PKCE enforced for public clients (`RequirePkceForPublicClients`, default true). |
| `refresh_token` | `OAuthGrantTypes.RefreshToken` | Refresh-token rotation enabled by default (`RotateRefreshTokens`, RFC 6819 §5.2.2.3). |
| `urn:ietf:params:oauth:grant-type:device_code` | `OAuthGrantTypes.DeviceCode` | RFC 8628; poll-interval enforced as `slow_down`. |

`password` (resource-owner password) is intentionally **not** supported — deprecated by OAuth 2.1.

## Wire integration
The handlers don't know about HTTP. A host typically does:

```csharp
var server = new OAuthServer(settings, jwtProvider, tokenOptions,
    clientStore, codeStore, refreshStore, deviceStore, consentStore,
    deviceVerificationUri: "https://auth.example.com/device");

// /token endpoint (POST application/x-www-form-urlencoded)
app.MapPost("/token", async (HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    var request = new TokenRequest { GrantType = form["grant_type"], /* ... */ };
    try
    {
        var response = await server.Token.HandleAsync(request);
        return Results.Json(new { access_token = response.AccessToken, /* ... */ });
    }
    catch (OAuthServerException ex)
    {
        return Results.Json(TokenErrorResponse.From(ex), statusCode: 400);
    }
});
```

## Patterns
- Token endpoint dispatch is a `switch` on `grant_type` — each branch is `HandleXxxAsync(client, request, ct)` and any protocol violation throws `OAuthServerException(ErrorCodes.X, "…")`. The host maps the exception to RFC 6749 §5.2 JSON via `TokenErrorResponse.From(ex)`.
- `IAsyncStore<T>` is the only persistence contract — any backend that implements it works. Tests use `InMemoryStore<T>`.
- `OAuthClient.ClientType` is immutable post-creation. Changing public ↔ confidential would invalidate stored hash/PKCE assumptions.
- Stored secrets are SHA-256 hashed (not PBKDF2/bcrypt) — they're high-entropy random values issued by the server, so a per-request constant-time SHA-256 compare is sufficient. Verification uses `CryptographicOperations.FixedTimeEquals`.
- Refresh tokens are stored as hashes too — the plaintext only exists transiently in `TokenResponse.RefreshToken` and in client memory.

## Maintenance

### README Updates
When making changes that affect the public API (handlers, settings, models, stores), update README.md.

### CLAUDE.md Updates
When adding new endpoints, grant types, or models, update this file to reflect the structure.

### Test Requirements
Every new public functionality must have corresponding unit tests in `Birko.Security.OAuth.Server.Tests` (xUnit + FluentAssertions). Each grant type must have at least: success path, expiry/invalid-grant path, and one client-misconfiguration path.
