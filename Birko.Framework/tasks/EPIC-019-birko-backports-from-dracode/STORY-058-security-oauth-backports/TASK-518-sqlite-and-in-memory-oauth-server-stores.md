---
id: TASK-518
parent: STORY-058
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Ship SQLite and in-memory implementations of the OAuth server stores

## Context

`Birko.Security.OAuth.Server` defines `IOAuthClientStore`, `IAuthorizationCodeStore`, `IDeviceCodeStore`,
`IRefreshTokenStore` and `IConsentStore` but ships no implementation, so every consumer writes its own. DraCode wrote both:
`DraCode.KoboldLair.Server/Auth/SqliteOAuthStores.cs` (276 lines, on `Birko.Data.SQL.SqLite`'s `AsyncSQLiteStore<T>`, with an
entity + JSON-column mapper for `OAuthClient`'s lists) and `InMemoryOAuthStores.cs` (84 lines), plus a DI helper
`AddOAuthServerStores(useSqlite, dbPath)`. They depend only on Birko types.

Adopted in the consumer by DraCode TASK-109 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] In-memory stores for all five interfaces ship with the OAuth server (or a small companion), usable as the default
- [x] SQLite stores ship in a companion package on `Birko.Data.SQL.SqLite` (tables created on first use)
- [x] A DI extension registers one shared instance of each (a device-code record is created in one request and read in later ones)
- [x] Tests per store: round-trip, expiry, revoke; SQLite data survives a new store instance on the same file
- [x] Start from DraCode's two files and keep their behaviour

## Out of scope

- Other SQL providers (MSSQL/MySQL/PostgreSQL) — follow-ups when a consumer needs them

## Human test plan

N/A — library behaviour, covered by unit tests.

## Outcome (2026-10-18)

Two companion projects, built from DraCode's two files.

- **`Birko.Security.OAuth.Server.InMemory`** (on `AsyncInMemoryStore<T>`) and **`Birko.Security.OAuth.Server.SqLite`**
  ship all five stores, with `AddOAuthServerInMemoryStores()` and `AddOAuthServerSqLiteStores(settings | path)`. Each
  registers one singleton per interface.
- **SQLite tables** are created on first use: `oauth_clients`, `oauth_auth_codes`, `oauth_refresh_tokens`,
  `oauth_device_codes` and `oauth_consents`. `OAuthClient`'s lists and `RefreshTokenRecord.Claims` (TASK-517) are kept
  in JSON columns.
- **Measured, not assumed:** the SQL column mapper *refuses* a `List`/`Dictionary` property (`FieldAttributeException`,
  since SH-H037), so nothing is dropped silently. DraCode's comment saying it drops them is stale. Consequence: DraCode's
  scalar `AsyncSQLiteStore<RefreshTokenRecord>` will throw at table load once it picks up TASK-517; adopting these
  stores fixes that.

**Single use is now the store's job, because the race was real.** Through HEAD's handler, 8 parallel redemptions of one
code, and 8 parallel refreshes of one token, issued more than one pair (3 of 3 runs, both backends).
- **New default interface methods.** `IAuthorizationCodeStore.TryRedeemAsync` and `IRefreshTokenStore.TryRevokeAsync`
  default to read-check-write, so existing custom stores compile, but only a single caller is safe with them. The
  shipped stores override them atomically: a conditional update (`… WHERE Used=0` / `Revoked=0`) on SQLite, a lock in
  memory. `RevokeFamilyAsync` is one set-based update.
- **How the token endpoint answers.** A lost code redemption is `invalid_grant`. A lost rotation (decided by the user)
  fails only that request, with `invalid_grant` and no family revoke, so two tabs refreshing together do not sign each
  other out. A token read after its rotation committed is still reuse and revokes the family.
- **Docs.** That limit is in docs/security.md: the server cannot tell a late tab from a replay.

**Behaviour differences from DraCode.** `UpdateAsync` of a missing row throws `EntityNotFoundException` and `SaveAsync`
upserts (DraCode's update created the row). A filtered `ReadAsync`/`CountAsync` on the two entity-backed stores throws
`NotSupportedException`; DraCode's `CountAsync` ignored the filter.

**Tests.** A shared store contract (`tests/Shared/OAuthServerStores`) runs against both backends. It covers round-trips
(collections and claims included), family revoke, 8-way concurrent claims, the PKCE code, device flow, federated claims
across rotation, reuse detection, concurrent redemption and refresh through the server, and expiry.
- SQLite only: survives a new instance on the same file, DI, the mapper measurement, update vs save.
- `RefreshRaceTests` (OAuth.Server.Tests) pins the user's decision: red with a family revoke on a lost claim.
- Mutation: with the claims made non-atomic, the 4 concurrency tests fail on each backend.

**Results.** OAuth.Server 61/61, InMemory 14/14, SqLite 20/20. Sandbox builds, with both projects imported (Sandbox
commit). Registered in the slnx, the code-workspace and CLAUDE-projects (catalog and Dependency Flow). Docs: README,
dependencies, security § Persistence, and the spec map's out-of-scope list (OAuth server area).

**Follow-up noted, not spawned:** SQLite `ExpiresAt` has no `[UtcField]`, as in DraCode. It is correct because the
server always writes UTC.
