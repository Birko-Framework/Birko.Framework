---
id: TASK-1013
parent: EPIC-019
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-988, TASK-990, TASK-995]
findings: []
pr: null
github-issue: null
jira-key: null
---

# OAuth server stores over any store factory

## Context

Asked for by DraCode on 2026-11-28 (session dracode-aa, DraCode TASK-228 split, owner-approved). DraCode files its own
switch task, depending on this one.

`Birko.Security.OAuth.Server` has two store sets, SQLite and InMemory. The SQLite stores are tied to `AsyncSQLiteStore<T>`.
Their one-time claims are SQLite conditional updates:
- `SqLiteAuthorizationCodeStore.TryRedeemAsync`: `UPDATE … SET Used = 1 WHERE Guid = @g AND Used = 0`;
- the refresh-token rotation claim in `SqLiteRefreshTokenStore.TryRevokeAsync`: `… SET Revoked = 1 WHERE Guid = @g AND Revoked = 0`.

So a consumer on PostgreSQL or MongoDB, choosing its backend through the unified store factory ([[TASK-990]]), cannot
keep its OAuth clients, codes, refresh tokens, device codes and consents in its own database. The SQLite client and
refresh-token stores also refuse predicates (`NotSupportedException`), so DraCode reads `oauth_clients` directly for
its "no two clients share a name" check (DraCode TASK-194).

## Acceptance criteria

- [ ] The five store interfaces (`IOAuthClientStore`, `IAuthorizationCodeStore`, `IRefreshTokenStore`, `IDeviceCodeStore`,
  `IConsentStore`) can be built from an `IStoreFactory`, for example `services.AddOAuthServerStores(factory)`.
- [ ] Code redemption and the refresh-token revoke are atomic on every backend the factory offers: of two concurrent
  claims, exactly one wins. A backend that cannot claim atomically is refused rather than given a racy claim.
- [ ] One shared contract test, run live per backend, covers the five stores and the two concurrent claims.
- [ ] The client store answers a lookup by name.
- [x] Decide with the user what happens to the SQLite and InMemory store packages: the SQLite package is deleted, InMemory
  stays as the test double (the user, 2026-11-28). The deletion waits for DraCode's switch (step 6).
- [ ] README, CLAUDE.md and docs updated; Symbio.Birko and DraCode's aggregator still build.

## Decisions (the user, 2026-11-28)

1. **The one-winner claim is a store capability**, not an OAuth adapter: `UpdateIfAsync` is lifted to an interface in
   `Birko.Data.Stores` and implemented on every backend that can make it atomic; JSON and XML refuse.
2. **Lists and claims persist through one entity per model, with a JSON text column, on every backend**, so the shape is
   the same on SQL and documents and the client store answers predicates (the name lookup).
3. **`Birko.Security.OAuth.Server.SqLite` is deleted; `.InMemory` stays** as the test double.

## Implementation plan

1. **`IAsyncConditionalUpdateStore<T>`** (Birko.Data.Stores): `Task<T?> UpdateIfAsync(Guid guid, Expression<Func<T, bool>>
   condition, Action<T> update, CancellationToken ct)` — read by id, apply `update` to the copy, and write it only where the
   stored row still matches `condition` (and the id); the updated entity, or null when the row is missing or no longer
   matches. Refused inside a unit of work where the backend cannot decide at once (as RavenDB and Cosmos DB already do).
   - SQL (all four providers, one implementation on `AsyncDataBaseStore`): `UPDATE … WHERE Guid = @g AND <condition>`,
     won when one row matched.
   - MongoDB: a replace filtered on id and the condition, won when it matched one document.
   - RavenDB, Cosmos DB, Elasticsearch: their existing `UpdateIfAsync(Guid, Func<T,bool>, …)` behind the compiled condition.
   - InMemory: under the store's lock.
   - JSON, XML: not implemented; the OAuth stores refuse such a factory at construction, by name.
   - A shared live contract: two (and n) concurrent claims on one row, exactly one wins, on every implementing backend.
2. **Entities**: `OAuthClientEntity` and `RefreshTokenEntity` (moved from the SQLite package to the core OAuth package),
   lists and claims as JSON text; the three scalar models persist as they are.
3. **Factory stores** in `Birko.Security.OAuth.Server`: the five interfaces over `IStoreFactory.GetAsyncStore<T>()`;
   `TryRedeemAsync` / `TryRevokeAsync` through `UpdateIfAsync` (`!Used` / `!Revoked`); `RevokeFamilyAsync` through the
   filtered `PropertyUpdate`; the client store translates predicates on its scalar members and adds `GetByNameAsync`.
4. **DI**: `services.AddOAuthServerStores(IStoreFactory)` (in `Birko.Security.OAuth.Server.AspNetCore` or wherever DI already
   lives for this family).
5. **Contract**: one shared OAuth store contract (the five stores, both claims under concurrency, the name lookup), run on
   InMemory and live on SQLite, PostgreSQL, MySQL, SQL Server, MongoDB, RavenDB, Cosmos DB and Elasticsearch.
6. **Then, after DraCode switches (its TASK-230)**: delete `Birko.Security.OAuth.Server.SqLite` and its tests, unregister it
   (slnx, workspace, aggregator, Sandbox import), docs. Deleting it first breaks DraCode, which builds the Framework
   working tree and uses `SqliteOAuthStores.cs`.

## Out of scope

- DraCode's switch to the new stores (its own task).
- The event store, which DraCode already moves through `GetEventStore(o => o.AddSql())` ([[TASK-995]]).

## Human test plan

N/A — fully covered by automated tests (live contract per backend).
