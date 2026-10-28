# Birko.Communication.OAuth.OpenIdConnect

## Overview
OpenID Connect web sign-in with a verified `id_token` (TASK-778): Google and Microsoft factories, any OIDC provider by
options. The framework owns the sign-in so every app verifies an `id_token` with one tested implementation; who may
sign in, user provisioning, cookies and session stay in the app (EPIC-020's boundary, moved 2026-10-28).

## Project Location
`Birko.Communication.OAuth.OpenIdConnect/`

## Namespace
`Birko.Communication.OAuth.OpenIdConnect`

## Components

### OpenIdProviderOptions.cs
- `OpenIdProviderOptions` — `Name` (the identity's provider), `MetadataAddress` (https discovery URL), `ClientId`,
  `ClientSecret`, `RedirectUri`, `Scope` (must include `openid`), `AdditionalIssuers`, `AllowedTenantIds`,
  `AuthorizationParameters` (not the reserved ones), `ClockSkew` (2 min), `MetadataRefreshInterval` (24 h),
  `MinimumMetadataRefreshInterval` (5 min), `RequestTimeout` (30 s). `Validate()` names the first unusable setting.

### GoogleOpenIdProvider.cs / MicrosoftOpenIdProvider.cs
- `GoogleOpenIdProvider.Create(clientId, secret, redirectUri, scope)` — Google's discovery document; `iss` with or
  without `https://` (`AdditionalIssuers = accounts.google.com`).
- `MicrosoftOpenIdProvider.Create(clientId, secret, redirectUri, tenant = common, allowedTenantIds, scope)` — the
  tenant is `common` / `organizations` / `consumers` / a GUID; anything else throws, so no other text reaches the URL.

### OpenIdMetadataCache.cs (internal) + `OpenIdProviderUnavailableException`
- The discovery document and key set, fetched on first use and after `MetadataRefreshInterval` on the injected
  `TimeProvider`. Not IdentityModel's `ConfigurationManager`, which may refresh in the background and serve the stale
  set meanwhile. Here a key miss refreshes at once, at most once per `MinimumMetadataRefreshInterval`, so forged
  tokens cannot make the app hammer the provider. A failed refresh keeps the cached set; with nothing cached it throws
  `OpenIdProviderUnavailableException` (an `OAuthException`).

### IdTokenValidator.cs
- `IdTokenValidator(options, http, clock?)` → `ValidateAsync(idToken, expectedNonce)` →
  `IdTokenValidationResult(Identity, Error, ProviderUnavailable)`.
  - `JsonWebTokenHandler` checks the signature (RS256 only, signed tokens required) and the audience.
  - Then, here: the issuer (exact, `AdditionalIssuers`, or the discovery issuer's `{tenantid}` replaced by the token's
    own `tid`, which must be a GUID), `AllowedTenantIds`, `azp` (required with several audiences, must be the client
    when present), `exp` (required) and `nbf` with `ClockSkew` on the clock, the nonce (constant time), a non-empty
    `sub`.
- `OpenIdIdentity(Provider, Subject, Issuer, Email, EmailVerified, Name, TenantId)`. `EmailVerified` only when
  `email_verified` or `xms_edov` is true (a bool, `"true"`, or `1`); Microsoft's `email` is unverified by default.

### OpenIdSignIn.cs
- `IOpenIdSignIn` / `OpenIdSignIn(options | validator, IOAuthStateStore, HttpClient, clock?)` — one per provider, a
  singleton.
- `BeginAsync(binding, data)` → `OpenIdSignInStart(AuthorizationUrl, State)`. The binding is required (login CSRF).
  The state stores the nonce, the PKCE verifier and `data`. The URL is built here, not by
  `OAuthClient.BuildAuthorizationUrl`, whose `ExtraParameters` also go into the token request.
- `CompleteAsync(OpenIdCallback(State, Code, Error, ErrorDescription), binding)` →
  `OpenIdSignInResult(Status, Identity, Data, Error, ErrorDescription)`, in this order:
  - no binding or state → `InvalidState`, nothing consumed;
  - the state consumed (once, with its binding) or not → `InvalidState`;
  - the provider's `error` → `ProviderRefused`;
  - no code, or the code refused → `CodeRejected`;
  - token endpoint 5xx, network, metadata → `ProviderUnavailable`;
  - no or bad `id_token` → `InvalidIdToken`.
- The exchange sends the verifier and the client secret (TASK-779).

## Dependencies
- **Birko.Communication.OAuth** — `IOAuthStateStore`, `OAuthClient`, `PkceChallenge`, `OAuthException` (and through
  it Birko.Configuration, Birko.Contracts)
- **NuGet:** `Microsoft.IdentityModel.JsonWebTokens` 8.* (dual CPM form; `Birko.Packages.props` entry). Kept out of
  `Birko.Communication.OAuth.Providers` so GitHub-only and Copilot consumers do not receive it.

## Consumers
- Presenter (TASK-058, Google and Microsoft sign-in)

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests live in
`tests/Birko.Communication.OAuth.OpenIdConnect.Tests`, against a fake provider with its own RSA keys.
