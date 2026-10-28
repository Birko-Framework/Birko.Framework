---
id: TASK-778
parent: null
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-10-28
depends-on: [TASK-779]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# OpenID Connect web sign-in, Google and Microsoft first, with the id_token verified

## Context

Requested for Presenter TASK-058 (`Consumers/Presenter/tasks`, FEATURE-001 sign-in and deck ownership): its users sign
in with Google or Microsoft, and it stores who they are as `provider:subject`. TASK-521 gave the framework GitHub's web
sign-in (state store, code exchanger, user client), but GitHub is plain OAuth: the user is read from an API with the
access token. Google and Microsoft are OpenID Connect providers, and the identity comes from a **signed `id_token`**,
which is only an identity once its signature, issuer, audience, lifetime and `nonce` are checked against the provider's
published keys. Writing that check in each app is the risky part of a login.

**This moves a boundary EPIC-020 drew.** EPIC-020 left "authentication / identity providers" out of scope because the
login is the host's job. The user decided on 2026-10-28 (in the Presenter session) that the framework carries the
OpenID Connect sign-in instead, so that Presenter, Reps and Symbio verify an `id_token` with one tested implementation.
Policy stays in the apps: who may sign in, user provisioning, what a verified email unlocks.

## Acceptance criteria

- [x] A new shared project `Birko.Communication.OAuth.OpenIdConnect`, so a GitHub-only or Copilot consumer of
      `Birko.Communication.OAuth.Providers` does not receive the OpenID Connect library; it owns
      ~~`Microsoft.IdentityModel.Protocols.OpenIdConnect`~~ **`Microsoft.IdentityModel.JsonWebTokens`** (`8.*`, dual
      CPM form, entry in `Birko.Packages.props`). Changed at implementation, see Outcome
- [x] Provider settings from the discovery document (authorization and token endpoints, signing keys, issuer), cached
      and refreshed by the library; Google and Microsoft factories (Microsoft's `common` / `organizations` /
      `consumers` / tenant id, its `{tenantid}` issuer checked against the token's `tid`)
- [x] A sign-in service: `BeginAsync(binding, data)` returns the authorization URL with `state`, `nonce` and PKCE;
      `CompleteAsync(callback, binding)` consumes the state, exchanges the code and returns the verified identity
      (`Provider, Subject, Issuer, Email, EmailVerified, Name, TenantId`) or a status saying why not
- [x] The id_token check: RS256 signature against the provider's keys (one key refresh on an unknown key), issuer,
      audience (and `azp` when there are several), expiry and not-before on an injectable `TimeProvider`, `nonce`,
      a non-empty `sub`
- [x] Every refusal path tested against a fake provider with its own signing key: forged signature, wrong issuer,
      wrong audience, expired, wrong or missing nonce, a state from another browser, a reused state, the provider's
      `error` callback, a code the provider refuses, `alg: none`
- [x] Docs: the project's README and CLAUDE.md, `CLAUDE-projects.md` (catalog + dependency flow),
      `docs/communication.md`, solution and workspace registration

## Out of scope

- An app's endpoints, cookie and session (Presenter TASK-058 owns its own)
- Who may sign in and user provisioning (policy, stays in apps, as in TASK-521)
- An ASP.NET Core authentication handler (`AddOpenIdConnect` exists; this is for apps that keep their own session)

## Implementation plan

1. Project skeleton: `.shproj` / `.projitems` with the dual `PackageReference`; `Birko.Packages.props` entry.
2. `OpenIdProviderSettings` + `GoogleOpenIdProvider` / `MicrosoftOpenIdProvider` factories.
3. `IdTokenValidator` over ~~`ConfigurationManager<OpenIdConnectConfiguration>`~~ an own discovery/key cache and `JsonWebTokenHandler`.
4. `OpenIdSignIn` (`IOpenIdSignIn`) over `IOAuthStateStore` and `OAuthClient`; status-enum result.
5. Test project with a fake provider (discovery, JWKS, token endpoint) signing with its own RSA key.
6. Docs and registration; regression of the OAuth test projects.

## Human test plan

Run in Presenter TASK-058: a real Google sign-in against the registered client signs in and stores `google:<sub>`.
The library itself is covered by unit tests against a fake provider.

## Outcome (2026-10-28)

Built in `Birko.Communication.OAuth.OpenIdConnect`. The task file was drafted by the Presenter session; the implementation
departs from its plan in two places, both decided here:

- **Own discovery / key cache instead of `ConfigurationManager`.** IdentityModel 8's manager may refresh in the
  background and hand back the stale configuration meanwhile, and follows its own clock. `OpenIdMetadataCache`
  (internal) fetches on the injected `TimeProvider`, refreshes after 24 h, and on a key miss refreshes at once, at
  most once per 5 minutes (so forged tokens cannot make the app hammer the provider). A failed refresh keeps the cached
  set. Deterministic in tests.
- **So the package is `Microsoft.IdentityModel.JsonWebTokens` 8.\*, not `Protocols.OpenIdConnect`**: nothing of the
  protocols package is used. A CPM consumer adds `<PackageVersion Include="Microsoft.IdentityModel.JsonWebTokens"
  Version="8.*" />` (or imports `Birko.Packages.props`).
- Lifetime is checked against the injected clock in the validator (`ValidateLifetime = false` in the handler), rather
  than relying on `TokenValidationParameters.TimeProvider`; `exp` is required.
- The authorization URL is built in `OpenIdSignIn`, not by `OAuthClient.BuildAuthorizationUrl`, whose
  `ExtraParameters` also go into the token request.
- Found on the way, filed and fixed separately: [[TASK-779]], the PKCE exchange dropped the client secret.

Public API: `OpenIdProviderOptions`, `GoogleOpenIdProvider.Create`, `MicrosoftOpenIdProvider.Create`,
`IOpenIdSignIn` / `OpenIdSignIn` (`BeginAsync`, `CompleteAsync`), `OpenIdSignInStart`, `OpenIdCallback`,
`OpenIdSignInResult`, `OpenIdSignInStatus`, `OpenIdIdentity`, `IdTokenValidator` / `IdTokenValidationResult`,
`OpenIdProviderUnavailableException`.

Tests: `tests/Birko.Communication.OAuth.OpenIdConnect.Tests` **61/61** against a fake provider with its own RSA keys,
covering every refusal path in the acceptance criteria plus `azp`, `nbf`, `sub`, the `{tenantid}` issuer, allowed
tenants, key rotation, the rate-limited refresh and a provider that is down. **Mutation-tested:** disabling any one of
ten checks (nonce, issuer, signed tokens required, key refresh, expiry, `azp`, the `tid` GUID check, audience, `sub`,
tenant allow-list) fails at least one test.

Registered: `.slnx`, `.code-workspace`, `Birko.Packages.props`, the Sandbox aggregator (Sandbox builds, no warning
from the new project), `README.md`, `CLAUDE-projects.md` (catalog and dependency flow), `docs/communication.md`,
`docs/dependencies.md`, `docs/specs/.map.yml` (beside the other OAuth projects in `ignore`). `audit-declarations.cs`:
nothing for the new project. EPIC-020's boundary is recorded in its EPIC.md.

No health check: whether an identity provider is up is not something a host monitors; it surfaces as
`ProviderUnavailable`.

Stays at `verify` until Presenter TASK-058 runs the human test plan (a real Google sign-in).
