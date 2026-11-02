---
id: TASK-805
parent: EPIC-020
feature: null
status: todo
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

- [ ] A `UserSession` model in Birko.Models.Users: user, login method, refresh token **hash** (never the token), tenant,
      device/user-agent label, created, last used, expires, revoked-at (+ reason). Mappings for every SQL provider and
      the document stores Birko.Models.Users supports.
- [ ] Issue / refresh / revoke as a small service consumers call (or extend the existing token flow): login creates a
      row; refresh finds the row by hash, ROTATES it (new token, same row, last-used updated); logout revokes this row;
      "log out everywhere", password change and admin password reset revoke all of the user's rows.
- [ ] **Reuse detection:** presenting an already-rotated token revokes that session (standard refresh-token rotation).
- [ ] The tenant lives on the row; a refresh issues into it while the user is still a member (Symbio TASK-1035's rule,
      moved here). A token in TASK-1035's `<random>.<tenant>` form, or a pre-session plain token, is still redeemed
      once and then moved onto a session row — no forced logout at the upgrade.
- [ ] Expired and revoked rows are pruned (a recurring job, or on write) so the table does not grow without bound.
- [ ] Listing a user's active sessions (device, last used) for a "where am I logged in" page; the UI page itself is a
      consumer task.
- [ ] Tests on a real database (SQLite at least): two devices both refresh; switch on one leaves the other's tenant;
      reuse of a rotated token revokes; logout-everywhere revokes all; a hashed token is never readable back.
- [ ] Symbio adoption filed as a Symbio task (AuthService / TokenIssuer / TenantService onto the service, the
      `UserLogin.RefreshToken` column retired after a release with both, DDL in Symbio's docs/92-pending-ddl.md).

## Out of scope

- The "active sessions" page in Symbio's admin UI (a Symbio task, after adoption).
- Device fingerprinting or location lookup beyond the user-agent label.

## Human test plan

- [ ] In Symbio, log in on two browsers (or a PC and a phone), switch tenant in one, wait out or force a refresh in both.
      Expected: both stay logged in, each in its own tenant.

## Implementation plan

_Populated by `/tasks plan TASK-805` — leave empty until then._
