# Birko.Communication.OAuth

## Overview
OAuth2 client library providing multiple grant type flows with automatic token caching, thread-safe refresh, and HttpClient integration via DelegatingHandler.

## Project Location
`Birko.Communication.OAuth/`

## Components

### OAuthSettings.cs
- **OAuthGrantType** — Enum: ClientCredentials, AuthorizationCode, AuthorizationCodePkce, DeviceCode, RefreshToken
- **OAuthSettings** — Extends `RemoteSettings` (Birko.Configuration). TokenEndpoint = Location, ClientId = UserName, ClientSecret = Password. Adds GrantType, AuthorizationEndpoint, DeviceAuthorizationEndpoint, Scope, RedirectUri, Audience, ExtraParameters, TokenExpiryBufferSeconds, TimeoutSeconds, DeviceCodePollingIntervalSeconds, DeviceCodeTimeoutSeconds

### OAuthToken.cs
- **OAuthToken** — AccessToken, TokenType, ExpiresAt, RefreshToken, Scope, IdToken, IsExpired(bufferSeconds)
- **DeviceAuthorizationResponse** — DeviceCode, UserCode, VerificationUri, VerificationUriComplete, ExpiresInSeconds, IntervalSeconds

### IOAuthClient.cs
- **IOAuthClient** — Interface: GetTokenAsync, RefreshTokenAsync, ExchangeCodeAsync, RequestDeviceAuthorizationAsync, PollDeviceTokenAsync, BuildAuthorizationUrl, ClearTokenCache

### OAuthClient.cs
- **OAuthClient** — Full implementation of IOAuthClient. Thread-safe token caching via SemaphoreSlim with double-check pattern. Supports all five OAuth2 flows. Automatic refresh token usage in GetTokenAsync. Parses standard OAuth2 JSON responses.
- Token requests send `Accept: application/json`. A 2xx response whose body carries `error` is an error response
  (`OAuthException`), and device polling continues through a 200 `authorization_pending` (GitHub answers errors with 200;
  both used to throw `KeyNotFoundException`, TASK-521).
- `TimeoutSeconds` sets `HttpClient.Timeout` only on a client it owns. A borrowed client gets the timeout per request
  and is never reconfigured (setting it threw once the client had sent a request, TASK-521).
- `ExchangeCodeAsync` sends `client_secret` whenever one is configured, with or without a PKCE verifier (TASK-779).
  It used to drop the secret under PKCE, and Google and Microsoft refuse a web client's exchange without it
  (`client_secret is missing`, `AADSTS7000218`). A public client configures no secret and sends none.

### OAuthDelegatingHandler.cs
- **OAuthDelegatingHandler** — DelegatingHandler that auto-attaches Bearer tokens from IOAuthClient. Retries once with fresh token on 401 Unauthorized.

### PkceChallenge.cs
- **PkceChallenge** — Generates SHA-256 PKCE code verifier + challenge pairs. Uses RandomNumberGenerator for secure random bytes. Base64url encoding per RFC 7636.

### OAuthStateStore.cs (TASK-521)
- **IOAuthStateStore** / **InMemoryOAuthStateStore** — short-lived, single-use sign-in `state` (RFC 6749 §10.12).
  `CreateAsync(binding, data)` / `TryConsumeAsync(state, binding)`: atomic single use, expiry on a `TimeProvider`,
  bounded (`MaxPending`, expired swept first, then the oldest evicted). **Bind it to the browser** (a random HttpOnly
  cookie's value): an unbound state does not stop login CSRF. A state is spent by its first presentation, even a refused one.

### OAuthCodeExchanger.cs (TASK-521)
- **IOAuthCodeExchanger** / **OAuthCodeExchanger** — exchanges a callback's code (with an optional PKCE verifier) through a
  fresh `OAuthClient` per call: no user's token is cached, and it is safe as a singleton over one HttpClient. The seam a
  sign-in callback's tests fake.

### OAuthException.cs
- **OAuthException** — Exception with ErrorCode, ErrorDescription, StatusCode from OAuth2 error responses.

## Dependencies
- **Birko.Configuration** — RemoteSettings base class
- **System.Text.Json** — JSON parsing (no external dependency)
- **System.Security.Cryptography** — PKCE generation

## Patterns
- Thread-safe token caching: SemaphoreSlim + double-check (same pattern as AzureKeyVaultSecretProvider)
- Settings hierarchy: OAuthSettings extends RemoteSettings for standard credential mapping
- HttpClient ownership: optional injection, disposes only if created internally
- DelegatingHandler: transparent token injection for any HttpClient pipeline

## Maintenance
- Token endpoint URL uses Location property from RemoteSettings
- ClientId uses UserName, ClientSecret uses Password from the settings hierarchy
- When adding new grant types, update OAuthGrantType enum, OAuthClient, and IOAuthClient interface
