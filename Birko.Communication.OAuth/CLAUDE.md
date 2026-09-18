# Birko.Communication.OAuth

## Overview
OAuth2 client library providing multiple grant type flows with automatic token caching, thread-safe refresh, and HttpClient integration via DelegatingHandler.

## Project Location
`C:\Source\Birko.Communication.OAuth\`

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

### OAuthDelegatingHandler.cs
- **OAuthDelegatingHandler** — DelegatingHandler that auto-attaches Bearer tokens from IOAuthClient. Retries once with fresh token on 401 Unauthorized.

### PkceChallenge.cs
- **PkceChallenge** — Generates SHA-256 PKCE code verifier + challenge pairs. Uses RandomNumberGenerator for secure random bytes. Base64url encoding per RFC 7636.

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
