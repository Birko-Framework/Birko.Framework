# Birko.Security.OAuth.Server.InMemory

## Overview
In-memory implementations of the five `Birko.Security.OAuth.Server` store interfaces (TASK-518, from DraCode's
`InMemoryOAuthStores.cs`), on `Birko.Data.InMemory`'s `AsyncInMemoryStore<T>`: thread-safe, hands out copies, keeps
nothing across a restart. The default for development and tests; use `Birko.Security.OAuth.Server.SqLite` to persist.

## Project Location
`C:\Source\Birko\Framework\Birko.Security.OAuth.Server.InMemory\`

## Components
- `Stores/InMemoryOAuthStores.cs`: `InMemoryOAuthClientStore`, `InMemoryAuthorizationCodeStore`,
  `InMemoryRefreshTokenStore`, `InMemoryDeviceCodeStore`, `InMemoryConsentStore`. The named lookups are the store
  interfaces' default methods.
  - `TryRedeemAsync` (codes) and `TryRevokeAsync` (refresh rotation) run under a per-instance lock, so a code or a
    refresh token is claimed exactly once within the instance. `RevokeFamilyAsync` is one filtered update.
- `Extensions/OAuthServerInMemoryServiceCollectionExtensions.AddOAuthServerInMemoryStores()`: one singleton per
  interface (a device-code record is created in one request and read in later ones).

## Dependencies
- Birko.Security.OAuth.Server (models, store interfaces), Birko.Data.InMemory (`AsyncInMemoryStore<T>`)
- Microsoft.Extensions.DependencyInjection.Abstractions, consumer-supplied (as for Birko.Data.SQL.SqLite's extension)

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests:
`tests/Birko.Security.OAuth.Server.InMemory.Tests` (the shared store contract in `tests/Shared/OAuthServerStores`).
