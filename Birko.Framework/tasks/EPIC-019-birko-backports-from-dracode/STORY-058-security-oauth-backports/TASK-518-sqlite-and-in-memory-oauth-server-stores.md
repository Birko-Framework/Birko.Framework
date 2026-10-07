---
id: TASK-518
parent: STORY-058
feature: null
status: todo
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

- [ ] In-memory stores for all five interfaces ship with the OAuth server (or a small companion), usable as the default
- [ ] SQLite stores ship in a companion package on `Birko.Data.SQL.SqLite` (tables created on first use)
- [ ] A DI extension registers one shared instance of each (a device-code record is created in one request and read in later ones)
- [ ] Tests per store: round-trip, expiry, revoke; SQLite data survives a new store instance on the same file
- [ ] Start from DraCode's two files and keep their behaviour

## Out of scope

- Other SQL providers (MSSQL/MySQL/PostgreSQL) — follow-ups when a consumer needs them

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
