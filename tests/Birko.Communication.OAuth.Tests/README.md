# Birko.Communication.OAuth.Tests

Unit tests for the Birko.Communication.OAuth project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0

## Test Classes

- **OAuthSettingsTests** — Settings hierarchy, property mapping (ClientId/UserName, etc.), defaults
- **OAuthTokenTests** — Token expiry, buffer, defaults
- **PkceChallengeTests** — PKCE generation, uniqueness, base64url encoding, correct length
- **OAuthExceptionTests** — Constructor variants, error code/description properties
- **OAuthClientTests** — Client credentials flow, token caching, code exchange, PKCE, authorization URL, device code, error handling, disposal
- **OAuthDelegatingHandlerTests** — Bearer token injection, 401 retry

## Running Tests

```bash
dotnet test Birko.Communication.OAuth.Tests.csproj
```

## License

MIT License - see [License.md](License.md)
