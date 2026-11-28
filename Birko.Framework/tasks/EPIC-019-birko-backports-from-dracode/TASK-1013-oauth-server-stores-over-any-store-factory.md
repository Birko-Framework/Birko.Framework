---
id: TASK-1013
parent: EPIC-019
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-28
depends-on: []
blocks: []
related: [TASK-988, TASK-990, TASK-995, TASK-1023]
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

- [x] The five store interfaces (`IOAuthClientStore`, `IAuthorizationCodeStore`, `IRefreshTokenStore`, `IDeviceCodeStore`,
  `IConsentStore`) can be built from an `IStoreFactory`, for example `services.AddOAuthServerStores(factory)`.
- [x] Code redemption and the refresh-token revoke are atomic on every backend the factory offers: of two concurrent
  claims, exactly one wins. A backend that cannot claim atomically is refused rather than given a racy claim.
- [x] One shared contract test, run live per backend, covers the five stores and the two concurrent claims.
- [x] The client store answers a lookup by name.
- [x] Decide with the user what happens to the SQLite and InMemory store packages: the SQLite package is deleted, InMemory
  stays as the test double (the user, 2026-11-28). The deletion waits for DraCode's switch (step 6).
- [x] README, CLAUDE.md and docs updated; Symbio.Birko and DraCode's aggregator still build.

## Decisions (the user, 2026-11-28)

1. **The one-winner claim is a store capability**, not an OAuth adapter: `UpdateIfAsync` is lifted to an interface in
   `Birko.Data.Stores` and implemented on every backend that can make it atomic; JSON and XML refuse.
2. **Lists and claims persist through one entity per model, with a JSON text column, on every backend**, so the shape is
   the same on SQL and documents and the client store answers predicates (the name lookup).
3. **`Birko.Security.OAuth.Server.SqLite` is deleted; `.InMemory` stays** as the test double.
4. **Elasticsearch: refresh after each OAuth write**, rather than refusing it or leaving the lag documented. Its searches
   (every lookup) see a write only after an index refresh, so a code redeemed right after it was issued was not found.
5. **Indexes on the lookup columns: a follow-up task**, [[TASK-1023]]. The entities live in the core package, which does
   not reference Birko.Data.SQL, so a new table gets none (an existing one keeps the SQLite package's).

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

## Evidence 2026-11-28 (steps 1–5; step 6 waits for DraCode)

**Step 1** (`7a13a8d9`): `IAsyncConditionalUpdateStore<T>` on SQL (all four), MongoDB, RavenDB, Cosmos DB, Elasticsearch and
InMemory. `ConditionalUpdateContract` passes on all nine; red first with the condition out of the write (SQLite 14 of 16
claims won).

**Steps 2–5:**
- Entities `OAuthClientEntity` / `RefreshTokenEntity` in `Birko.Security.OAuth.Server.Models.Persistence`, with the
  SQLite package's table and column names; the three scalar models got their `[Table]` names.
- Five factory stores (`Stores/Factory`); predicates over the model are translated to the entity, and one on a list or
  claims member is refused by name. Claims go through `UpdateIfAsync`. Every write calls `MakeVisibleAsync`, which
  refreshes an `IAsyncRefreshableStore` (new in Birko.Data.Stores, implemented by `AsyncElasticSearchStore`).
- `IOAuthClientStore.GetByNameAsync`: a default method, overridden by the SQLite package (which refuses predicates).
- `services.AddOAuthServerStores(IStoreFactory)` in the core package, building all five first, so a refused backend
  (JSON, XML) registers nothing.

**Tests:** `tests/Birko.Security.OAuth.Server.StoreFactory.Tests` 264/264. That is the shared OAuth contract (every store,
lookups, lists and claims, claims under 2/8/16 callers, the server's code and refresh flows) on InMemory, SQLite and live
PostgreSQL, MySQL, SQL Server, MongoDB, RavenDB, Cosmos DB and Elasticsearch, plus a test that an old SQLite OAuth
database is read and written by the factory stores.
- Red first: read-check-write claims failed 62 of 72 concurrency checks on every backend.
- With the Elasticsearch refresh turned off, 26 of its 28 checks failed; nothing settles between the stores' writes and
  reads now, and the server flows run there.
- Also passing: OAuth.Server 68, AspNetCore 13, InMemory 26, SqLite 32, Elasticsearch 746 (one earlier run failed one
  test, which passed on rerun: the known reindex timing flake).
- No warning on a new file; DraCode.Birko and Symbio.Birko build.

**Not live in CI:** the new test project needs all nine backends at once, while `live-tests.yml` runs one job per
backend; the TASK-990 store-factory tests are not in it either.

## Evidence 2026-11-28 (step 6)

DraCode reported its switch merged (DraCode a2db45f, PR #95: nothing references the SQLite or InMemory OAuth packages;
its suite 774 passed, 90/90 live on PostgreSQL and MongoDB). Then:
- `Birko.Security.OAuth.Server.SqLite` and its tests deleted; unregistered from the slnx, the workspace and Birko.Sandbox
  (the framework aggregator never imported it).
- The legacy-database test kept, without the package: `Fixtures/LegacySqLiteOAuthDatabase.sql` is a `sqlite3 .dump` of a
  database the package wrote just before the deletion; the test loads it, reads every store through the factory, claims
  a code and a refresh token, updates a client, and checks the writes landed in the package's columns. Red: with the
  fixture's `CollectionsJson` column renamed it fails ("no such column").
- Docs: root README, CLAUDE-projects, dependencies, security, the OAuth server, AspNetCore and InMemory docs; the test
  project's CLAUDE.md also lost its stale "Elasticsearch settles and skips the server flows" line.
- StoreFactory 264, OAuth.Server 68, InMemory 26, AspNetCore 13 pass; the solution, DraCode.Birko and Birko.Sandbox
  build; spec-links clean.

## Out of scope

- DraCode's switch to the new stores (its own task).
- The event store, which DraCode already moves through `GetEventStore(o => o.AddSql())` ([[TASK-995]]).

## Human test plan

N/A — fully covered by automated tests (live contract per backend).
