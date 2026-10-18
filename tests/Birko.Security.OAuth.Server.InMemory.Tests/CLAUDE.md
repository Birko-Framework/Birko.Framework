# Birko.Security.OAuth.Server.InMemory.Tests

## Overview
Tests of the in-memory OAuth server stores (TASK-518). The bulk is the shared contract
`tests/Shared/OAuthServerStores/OAuthStoreContract.cs`, linked into both store test projects:
- every model round-trips (client collections and refresh claims included), and a family is revoked alone;
- a code and a refresh token are each claimed exactly once by 8 concurrent callers;
- a real `OAuthServer` runs authorization_code with PKCE, the device flow, federated issue with claims surviving
  rotation, reuse detection, concurrent redemption and refresh, and expiry.

Also: the DI registration gives one shared instance per store.

## Dependencies
xUnit, FluentAssertions, Microsoft.Extensions.DependencyInjection; imports Birko.Security.OAuth.Server.InMemory,
Birko.Security.Jwt and their dependencies.

## Maintenance
See [CLAUDE-maintenance.md](../../Birko.Framework/CLAUDE-maintenance.md).
