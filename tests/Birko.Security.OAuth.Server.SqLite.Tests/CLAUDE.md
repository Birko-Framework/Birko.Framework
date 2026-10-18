# Birko.Security.OAuth.Server.SqLite.Tests

## Overview
Tests of the SQLite OAuth server stores (TASK-518), each test on its own database file. The bulk is the shared contract
`tests/Shared/OAuthServerStores/OAuthStoreContract.cs`, linked into both store test projects:
- every model round-trips (client collections and refresh claims included), and a family is revoked alone;
- a code and a refresh token are each claimed exactly once by 8 concurrent callers;
- a real `OAuthServer` runs authorization_code with PKCE, the device flow, federated issue with claims surviving
  rotation, reuse detection, concurrent redemption and refresh, and expiry.

SQLite only:
- data survives a new store instance on the same file;
- the DI registrations (by settings and by path);
- the column-mapper measurement behind the entity mappings;
- Update vs Save on a missing client.

## Dependencies
xUnit, FluentAssertions, Microsoft.Extensions.DependencyInjection; imports Birko.Security.OAuth.Server.SqLite,
Birko.Security.Jwt and their dependencies.

## Maintenance
See [CLAUDE-maintenance.md](../../Birko.Framework/CLAUDE-maintenance.md).
