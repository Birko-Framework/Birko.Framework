# Birko.Security.OAuth.Server.Tests

xUnit + FluentAssertions tests for [`Birko.Security.OAuth.Server`](../Birko.Security.OAuth.Server) — the OAuth2 authorization server.

## Coverage

- **`PkceValidatorTests`** — RFC 7636 Appendix B canonical S256 vector.
- **`ClientSecretHasherTests`** — hash + fixed-time `Verify`.
- **`RandomStringGeneratorTests`** — Base64Url token shape; user-code unambiguous alphabet.
- **`TokenEndpointHandlerTests`** — one success + one failure per grant type (`client_credentials`, `authorization_code` + PKCE, `refresh_token`, device code), plus unsupported-grant.
- **`AuthorizationEndpointHandlerTests`** — authorize (first-time / prior-consent), consent (approved / denied), PKCE-required.
- **`DeviceAuthorizationHandlerTests`** — issue codes, Approve/Deny, unauthorized-client.
- **`ClientRegistrationHandlerTests`** — RFC 7591 register / get / update / delete.

Internal helpers are reachable because the `Birko.Security.OAuth.Server` shared project is recompiled into this test assembly. Each test spins up a fresh `TestServer` fixture (in-memory stores + `JwtTokenProvider` + a frozen `TestDateTimeProvider`); expiry tests advance the clock via `fixture.Clock.Advance(...)`.

## Test framework

- xUnit
- FluentAssertions

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
