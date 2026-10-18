# Birko.Security.OAuth.Server.SqLite

## Overview
SQLite implementations of the five `Birko.Security.OAuth.Server` store interfaces (TASK-518, from DraCode's
`SqliteOAuthStores.cs`). Tables are created on first use (the stores' lazy schema-ensure), so nothing needs an init
call at startup.

## Project Location
`C:\Source\Birko\Framework\Birko.Security.OAuth.Server.SqLite\`

## Components
- `Stores/SqLiteOAuthScalarStores.cs`: `SqLiteAuthorizationCodeStore` (`oauth_auth_codes`), `SqLiteDeviceCodeStore`
  (`oauth_device_codes`), `SqLiteConsentStore` (`oauth_consents`), each an `AsyncSQLiteStore<T>` over the model as it
  is. The table names are registered once by `SqLiteOAuthTables.EnsureRegistered` (the models carry no `[Table]`).
  - `TryRedeemAsync` is one conditional update, `… SET Used = 1 WHERE Guid = @g AND Used = 0`: of two concurrent
    redemptions exactly one matches a row (CR-L351).
- `Stores/SqLiteOAuthClientStore.cs` (`oauth_clients`) and `Stores/SqLiteRefreshTokenStore.cs`
  (`oauth_refresh_tokens`): the SQL column mapper has no mapping for `OAuthClient`'s `List<string>` properties or
  `RefreshTokenRecord.Claims` (TASK-517) and refuses them at table load (`FieldAttributeException`, SH-H037), so these
  two map to `Models/OAuthClientEntity` / `Models/RefreshTokenEntity` with a JSON column. A filtered
  `ReadAsync` / `CountAsync` is refused on them (a model predicate cannot be translated to the entity's columns); the
  server only uses the named lookups. `UpdateAsync` of a row that is not stored throws `EntityNotFoundException`
  (TASK-555), `SaveAsync` upserts.
  - `TryRevokeAsync` (refresh rotation) is one conditional update on `Revoked = 0`; `RevokeFamilyAsync` is one
    set-based update.
- `Extensions/OAuthServerSqLiteServiceCollectionExtensions.AddOAuthServerSqLiteStores(settings | path)`: one singleton
  per interface, all on one database file; the path overload creates the directory.

`oauth_clients` keeps DraCode's column names, so its existing table is read as is. DraCode's `oauth_refresh_tokens`
lacks `ClaimsJson`.

## Dependencies
- Birko.Security.OAuth.Server, Birko.Data.SQL.SqLite (and through it Birko.Data.SQL)
- Microsoft.Extensions.DependencyInjection.Abstractions, consumer-supplied

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests:
`tests/Birko.Security.OAuth.Server.SqLite.Tests` (the shared store contract in `tests/Shared/OAuthServerStores`).
