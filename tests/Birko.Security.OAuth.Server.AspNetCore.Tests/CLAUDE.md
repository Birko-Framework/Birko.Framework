# Birko.Security.OAuth.Server.AspNetCore.Tests

## Overview
Tests of `MapOAuthServer` (TASK-519) over a real `TestServer`, with the in-memory OAuth stores and a header-driven test
authentication scheme (`X-Test-User`, `X-Test-Roles`).
- **`/token`:** Basic and body client authentication, snake_case and `no-store`, Basic form-decoding, 401 + challenge
  after Basic vs 400 in the body, malformed or doubled authentication refused, a non-form body, an unknown grant, and
  `GrantHandlers` taking over a grant and adding one.
- **Device flow:** end to end with the approver taken from the principal, and its error shapes.
- **`/authorize`:** requires a user; reports consent or a code. Redirect mode sends a 302 to the registered URI and never
  redirects an error.
- **Registration:** off by default; on, gated by the app's policy, with string and numeric `clientType` and disable.

## Dependencies
xUnit, FluentAssertions, Microsoft.AspNetCore.TestHost (FrameworkReference Microsoft.AspNetCore.App); imports
Birko.Security.OAuth.Server(.InMemory, .AspNetCore), Birko.Security.Jwt and their dependencies.

## Maintenance
See [CLAUDE-maintenance.md](../../Birko.Framework/CLAUDE-maintenance.md).
