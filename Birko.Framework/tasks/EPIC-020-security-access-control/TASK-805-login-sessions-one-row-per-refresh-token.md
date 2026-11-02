---
id: TASK-805
parent: EPIC-020
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-02
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Login sessions: one row per refresh token (several devices, hashed tokens, per-device revoke)

## Context

Asked for by the user 2026-11-02, from Symbio TASK-1035.

`Birko.Models.Users.UserLogin` holds ONE refresh token per login (`RefreshToken`, `RefreshTokenExpiry`). Every issuance
— a login, a refresh rotation, a tenant switch — overwrites it, so only the newest device can refresh: log in on a phone
and the PC is logged out at its next refresh (Symbio's access token lives 60 min); switch tenant on one device and the
other loses its session. The token is also stored in plain text, so a database leak hands out usable refresh tokens.

Symbio TASK-1035 fixed the visible tenant bug inside that model by carrying the tenant in the token
(`<random>.<tenantGuid:N>`, Symbio `RefreshTokenTenant`). That is a convention inside an opaque string; a session row is
its proper home.

## Acceptance criteria

- [x] A `UserSession` model in Birko.Models.Users: user, login method, refresh token **hash** (never the token), tenant,
      device/user-agent label, created, last used, expires, revoked-at (+ reason). Mappings for every SQL provider and
      the document stores Birko.Models.Users supports.
- [x] Issue / refresh / revoke as a small service consumers call (or extend the existing token flow): login creates a
      row; refresh finds the row by hash, ROTATES it (new token, same row, last-used updated); logout revokes this row;
      "log out everywhere", password change and admin password reset revoke all of the user's rows.
- [x] **Reuse detection:** presenting an already-rotated token revokes that session (standard refresh-token rotation).
- [x] The tenant lives on the row; a refresh issues into it while the user is still a member (Symbio TASK-1035's rule,
      moved here). A token in TASK-1035's `<random>.<tenant>` form, or a pre-session plain token, is still redeemed
      once and then moved onto a session row — no forced logout at the upgrade.
- [x] Expired and revoked rows are pruned (a recurring job, or on write) so the table does not grow without bound.
- [x] Listing a user's active sessions (device, last used) for a "where am I logged in" page; the UI page itself is a
      consumer task.
- [x] Tests on a real database (SQLite at least): two devices both refresh; switch on one leaves the other's tenant;
      reuse of a rotated token revokes; logout-everywhere revokes all; a hashed token is never readable back.
- [x] Symbio adoption filed as a Symbio task (AuthService / TokenIssuer / TenantService onto the service, the
      `UserLogin.RefreshToken` column retired after a release with both, DDL in Symbio's docs/92-pending-ddl.md).

## Out of scope

- The "active sessions" page in Symbio's admin UI (a Symbio task, after adoption).
- Device fingerprinting or location lookup beyond the user-agent label.

## Human test plan

- [ ] In Symbio, log in on two browsers (or a PC and a phone), switch tenant in one, wait out or force a refresh in both.
      Expected: both stay logged in, each in its own tenant.

## Decisions (the user, 2026-11-02)

1. **Placement:** inside Birko.Models.Users (model, filter, view model, service) and Birko.Models.Users.SQL (mapping).
   No new projects. Birko.Models.Users gains a dependency on Birko.Data.Stores; measured first: all five consumer
   aggregators that import Birko.Models.Users already import Birko.Data.Stores (Symbio, Presenter, Reps, FisData.Stock
   .Angular, the Sandbox). The clock is the BCL `TimeProvider`, not Birko.Time.Abstractions, which four of the five do
   not import.
2. **Tenant rule:** the framework checks the user is still a member (`UserTenants`), and the app may add its own check
   through a hook (Symbio: the tenant is still active). If either says no, the refresh drops the tenant and the app picks
   one as at login.
3. **Old tokens:** the framework redeems a token found on `UserLogin.RefreshToken` once, moves it onto a session row and
   clears the column. The app passes an optional parser for a tenant carried in the token (Symbio's `<random>.<tenant>`).

## Implementation plan

- **`UserSession`** (`Models/UserSession.cs`, `AbstractLogModel`, `IRelatedToUser`): `UserGuid`, `UserLoginGuid?`,
  `TokenHash`, `PreviousTokenHash?`, `TenantGuid?`, `DeviceLabel?`, `LastUsedAt`, `ExpiresAt`, `RevokedAt?`,
  `RevokedReason?`. The view model and `ILoadable` carry no hash, so a session listed to a page cannot leak one. Filter
  by user, tenant, active-at.
- **`UserSessionService`** (`Sessions/`), over `IAsyncBulkStore<UserSession>`, optionally `IAsyncBulkStore<UserTenant>`
  (the membership check) and `IAsyncBulkStore<UserLogin>` (old tokens), with `UserSessionOptions` (lifetime, the
  tenant hook, the legacy-tenant parser, revoked-row retention) and a `TimeProvider`:
  - `CreateAsync(user, tenant?, login?, device?)`: a new row, the token returned once (32 random bytes, base64url);
    the row stores SHA-256 of it. Prunes the user's own dead rows on the way.
  - `RefreshAsync(token)`: find by hash; rotate on the SAME row as a claim (a filtered update
    `Guid == g && TokenHash == presented && RevokedAt == null`, then a re-read: the Grants invitation pattern, one
    conditional statement on SQL). Lost claim = a concurrent refresh, refused without revoking. A token matching a row's
    `PreviousTokenHash` is a reuse: that session is revoked. Not found by hash → the old `UserLogin.RefreshToken`
    lookup, moved onto a new row. The tenant is kept only while member + hook allow it.
  - `SwitchTenantAsync(session, user, tenant)`: moves one session (and rotates), the other devices keep theirs. The
    results expose the session id so the app can put it in the access token (`sid`) and find it at logout/switch.
  - `RevokeAsync(session, user, reason)`, `RevokeAllAsync(user, reason, except?)` (also clears old
    `UserLogin.RefreshToken` values, so an unmigrated token dies too), `ListActiveAsync(user)` (view models),
    `PruneAsync()` (expired rows, and revoked rows past the retention).
- **`UserSessionMapping`**: `UserSessions`, UNIQUE `TokenHash`, indexes on `PreviousTokenHash` and `UserGuid`.
- **Tests on SQLite** (Birko.Models.Users.SQL.Tests): two devices both refresh; a switch on one leaves the other's
  tenant; a rotated token presented again revokes its session; a lost claim does not; logout everywhere revokes all
  and kills an old column token; the stored hash is not the token and the view model has none; an old plain token and
  an old `<random>.<tenant>` token are redeemed once onto a row; the tenant dropped when the user left it or the hook
  says no; expired refused; prune.
- Docs: both CLAUDE.md / README, CLAUDE-projects.md dependency line, framework CLAUDE.md Recent Updates.
- Then file the Symbio adoption task (via the Symbio session).

## Resolution (2026-11-02)

Built as planned, all in Birko.Models.Users and Birko.Models.Users.SQL:

- `Models/UserSession.cs`, `ViewModels/UserSession.cs` (no hash), `Filters/UserSession.cs`.
- `Sessions/UserSessionService.cs` with `UserSessionOptions` (`Lifetime`, `RevokedRetention`, `UserAllowed`,
  `TenantAllowed`, `LegacyTenant`, `MaxDeviceLabelLength`), `SessionRevokeReasons`, `IssuedSession`,
  `SessionRefreshResult` / `SessionRefreshStatus` (Refreshed, Invalid, Expired, Revoked, Reused, Raced, UserRefused).
  `CreateAsync`, `RefreshAsync`, `SwitchTenantAsync`, `RevokeAsync`, `RevokeByTokenAsync`, `RevokeAllAsync`
  (`exceptSessionGuid`; also clears old `UserLogin.RefreshToken` values), `ListActiveAsync`, `PruneAsync`, `HashToken`.
- `Mappings/UserSessionMapping.cs`: `UserSessions`, `UX_UserSessions_TokenHash`, `IX_UserSessions_PreviousTokenHash`,
  `IX_UserSessions_UserGuid`.
- "The document stores Birko.Models.Users supports": it has no document-store mappings (only the SQL sibling), so the
  model is used as is there. The claim is one conditional statement only where a filtered update is (SQL); stated in
  the service's remarks.
- Not done, on purpose: an older previous token (two rotations back) is `Invalid`, not a reuse; only the latest previous
  hash is kept. No grace window for a reuse right after a rotation: two tabs racing get `Raced` only while the loser's
  read precedes the winner's write.

**Proof.** `tests/Birko.Models.Users.SQL.Tests/UserSessionServiceTests.cs`, 18 tests on SQLite: two devices both
refresh; a switch on one leaves the other's tenant; a switch of another user's session is refused; reuse revokes; a lost
claim is `Raced` and revokes nothing (forced by an intercepting store); revoke-all, with and without an excepted session,
and it kills an old column token; logout of one device; the stored hash is not the token and the view model has no
hash; an old plain token and an old `<random>.<tenant>` token are redeemed once; an expired old token is not moved; the
tenant dropped when the user left it or the hook says no; a refused user is signed out; expired refused; prune and
prune-at-sign-in; the indexes exist. Nine mutations (claim filter, reuse revoke, revoke-all column, membership check,
prune retention, except, old column cleared, old tenant, switch tenant), each caught; the prune test first let its
mutation through (the revoked row had also expired) and was fixed. Users.SQL.Tests 34/34, Users.Tests 9/9, the
solution builds, and the consumer smoke harness (Birko.Sandbox) reports 20 verified, 0 failed.

Human test pending: needs Symbio's adoption, filed as Symbio **TASK-1039** "Adopt Birko login sessions" (2026-11-02,
Symbio main 0369f840). Its human test is this task's.
