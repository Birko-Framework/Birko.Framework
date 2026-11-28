---
id: TASK-1013
parent: EPIC-019
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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
- [ ] Decide with the user what happens to the SQLite and InMemory store packages (kept, or replaced by the factory ones).
- [ ] README, CLAUDE.md and docs updated; Symbio.Birko and DraCode's aggregator still build.

## Out of scope

- DraCode's switch to the new stores (its own task).
- The event store, which DraCode already moves through `GetEventStore(o => o.AddSql())` ([[TASK-995]]).

## Human test plan

N/A — fully covered by automated tests (live contract per backend).
