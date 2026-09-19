# Birko.Security.OAuth.Server.Tests

## Overview
xUnit + FluentAssertions tests for `Birko.Security.OAuth.Server`. Every grant type and every endpoint handler has at least success-path and protocol-error-path coverage. Internal helpers (`PkceValidator`, `ClientSecretHasher`, `RandomStringGenerator`) are tested directly — they're `internal` to the OAuth.Server shared project, which is recompiled into this test assembly, so the test code can see them.

## Project Location
`tests/Birko.Security.OAuth.Server.Tests/`

## Structure
```
Birko.Security.OAuth.Server.Tests/
├── InMemoryStore.cs                       — Generic IAsyncStore<T> backed by ConcurrentDictionary; per-entity subclasses
├── TestServer.cs                          — Fixture: in-memory stores + JwtTokenProvider + TestDateTimeProvider
├── PkceValidatorTests.cs                  — RFC 7636 Appendix B canonical vector
├── ClientSecretHasherTests.cs             — Hash + fixed-time Verify
├── RandomStringGeneratorTests.cs          — Base64Url shape; user-code unambiguous-alphabet
├── TokenEndpointHandlerTests.cs           — One success + one failure per grant type, plus unsupported-grant
├── AuthorizationEndpointHandlerTests.cs   — Authorize (first-time / prior-consent) + Consent (approved / denied) + PKCE-required
├── DeviceAuthorizationHandlerTests.cs     — Issue codes, Approve/Deny, unauthorized-client
└── ClientRegistrationHandlerTests.cs      — RFC 7591 register / get / update / delete
```

## Test Patterns
- Each test instantiates a fresh `TestServer` fixture — no shared mutable state across tests.
- Time is frozen via `TestDateTimeProvider`; advance the clock with `fixture.Clock.Advance(...)` for expiry tests.
- Confidential clients are seeded via `fixture.RegisterConfidentialClient(id, secret, ...grantTypes)`.
- Public (PKCE-only) clients use `fixture.RegisterPublicClient(id, ...grantTypes)`.
- Errors are asserted on `OAuthServerException.ErrorCode` against constants in `OAuthErrorCodes`.

## Notes
- The fixture uses `JwtTokenProvider` (real JWT). Tests don't decode the JWT — they verify shape (`access_token`, `token_type`, `expires_in`, `refresh_token`, `scope`) and the persistence side-effects (codes marked used, refresh tokens revoked on rotation, consents stored).
