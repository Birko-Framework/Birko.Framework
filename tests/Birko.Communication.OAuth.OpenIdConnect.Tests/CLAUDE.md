# Birko.Communication.OAuth.OpenIdConnect.Tests

## Overview
Unit tests for Birko.Communication.OAuth.OpenIdConnect (TASK-778).

## Project Location
`tests/Birko.Communication.OAuth.OpenIdConnect.Tests/`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- `FakeOpenIdProvider` — an `HttpMessageHandler` serving a discovery document, a key set and a token endpoint at
  `https://idp.test`, signing with RSA keys of its own (`NewKey`, `Sign`, `Unsigned` for `alg: none`). Code `good`
  gets `NextIdToken`, any other code `invalid_grant`. It counts discovery and key-set requests.
- `SignInHarness` — an `OpenIdSignIn` over it on a `ManualClock`. `SignInWithClaimsAsync(change, key)` runs a whole
  sign-in with this sign-in's valid claims, changed.
- `OpenIdSignInTests` — the URL, the round trip and the exchange body. Then each refusal:
  - the token: forged signature, `alg: none`, issuer, audience, `azp`, expiry and skew, `nbf`, nonce, `sub`, no
    `id_token`; `email_verified` / `xms_edov`;
  - the state: another browser's, reused, unknown, expired;
  - the provider: its error callback, a refused code, a 5xx.
- `IssuerAndTenantTests` — Google's two issuers; Microsoft's `{tenantid}` issuer and `tid`; allowed tenants.
- `SigningKeyRefreshTests` — caching, key rotation, the rate-limited refresh, the refresh interval, a provider that is
  down.
- `ProviderOptionsTests` — the factories and `Validate()`.

Every security check was mutation-tested at TASK-778. Disabling any one of these fails at least one test: nonce,
issuer, the signed-token requirement, key refresh, expiry, `azp`, the `tid` GUID check, audience, `sub`, the tenant
allow-list.
