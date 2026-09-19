# Birko.Communication.OAuth.Providers.Tests

## Overview
Unit tests for Birko.Communication.OAuth.Providers — the pre-configured OAuth provider factories.

## Project Location
`tests/Birko.Communication.OAuth.Providers.Tests/`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- `GitHubOAuthProviderTests` — CR-L077 coverage: `CreateDeviceFlowSettings` field values (GitHub
  endpoints, `GrantType = DeviceCode`, default scope `read:user`, polling = 5, timeout = 600) and that
  `CreateDeviceFlowClient` returns a non-null `IOAuthClient`. Also guards the CR-L076 single-source-of-
  truth (the client factory delegates to the settings factory).
