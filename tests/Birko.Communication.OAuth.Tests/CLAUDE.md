# Birko.Communication.OAuth.Tests

## Overview
Unit tests for Birko.Communication.OAuth — OAuth2 client library.

## Project Location
`C:\Source\Birko.Communication.OAuth.Tests\`

## Test Classes
- **OAuthSettingsTests** — Settings hierarchy, property aliases, defaults
- **OAuthTokenTests** — Token expiry logic with and without buffer
- **PkceChallengeTests** — PKCE generation, base64url encoding, uniqueness
- **OAuthExceptionTests** — Exception constructors, error properties
- **OAuthClientTests** — All flows (client credentials, auth code, PKCE, device code), caching, error handling, disposal
- **OAuthDelegatingHandlerTests** — Bearer token injection, 401 retry logic

## Dependencies
- Birko.Contracts (projitems)
- Birko.Configuration (projitems)
- Birko.Communication.OAuth (projitems)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Test Helpers
- **FakeHttpHandler** — Configurable HttpMessageHandler stub for testing HTTP calls
- **InspectingHandler** — Handler that captures request details for assertions

## Maintenance
- Add tests for new OAuth2 grant types or features
- FakeHttpHandler tracks RequestCount and LastRequestBody for verification
