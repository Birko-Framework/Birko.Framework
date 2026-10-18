---
id: TASK-524
parent: STORY-059
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

# Birko.Web.Shell auth: OAuth redirect parsing, token expiry and scheduled refresh

## Context

`Birko.Web.Shell/src/auth` has `createAuthStore` and guards but nothing for an OAuth redirect sign-in. DraCode TASK-056
plans app-side code for it: read `#access_token=…&refresh_token=…` from the URL fragment after a provider redirect, decode
the JWT `exp`, and refresh shortly before expiry through `ApiClient`'s existing `onRefreshToken` / `onUnauthorized` hooks.
None of that is app-specific.

Consumer side: DraCode `EPIC-018 / STORY-039` (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] A helper parses (and then clears from the address bar) an auth fragment into the auth store
- [x] Token expiry is read from the JWT `exp`; a scheduler refreshes ahead of expiry and on a 401, through a pluggable refresh call
- [x] The auth store's token is what `ApiClient`, `WsClient` and `SseClient` send
- [x] Tests for parsing, expiry and the refresh schedule

## Out of scope

- A login page or provider buttons (apps)

## Human test plan

N/A — covered by unit tests.

## Outcome (2026-10-18)

Released in **Birko.Web v0.1.11** (Web `1ae098a`, tag `v0.1.11`), with the gate in the Playground (`c4b5ddf`).

- **`readAuthFragment()`** parses `#access_token=…&refresh_token=…&expires_in=…` and clears it with `replaceState`,
  keeping path and query. A hash route (`#/orders`) or a fragment without `access_token` is left alone. An `error=`
  fragment (a refused sign-in) is cleared and returns null.
- **`createAuthSession({ auth, refresh })`.**
  - It refreshes `leadMs` (60 s) before the JWT's `exp`, after a 401 (as ApiClient's `onRefreshToken`, single flight),
    and `retryMs` (30 s) after a refresh that rejected.
  - A refresh that resolves null clears the store and calls `onSessionEnded`. A non-rotated refresh token is kept. A delay
    past `setTimeout`'s ~24.8-day ceiling is capped and re-waited, never fired early.
  - `getToken` is the token source for ApiClient, WsClient and SseClient.
- **`tokenExpiresAt` / `decodeJwtPayload`** read base64url. **Fixed on the way:** `setAuth` used a bare `atob`, so a
  payload containing `-` or `_` yielded no claims at all (no userId, permissions or tenant).
- **Gate:** `auth-session-smoke` in the Playground, 32/32 (parsing, expiry, schedule with injected clock and timers,
  ApiClient's 401 retry carrying the refreshed token, SseClient's token). Red: the `setAuth` check fails on the old
  `auth-store.ts`; the rest is new API. `tsc` is clean on the new files.
- **Full `verify.mjs`:** every other suite passes apart from focus- and ribbon-geometry checks that fail at random
  (2, 3, 6 or 8 per run, different each time). They fail the same way with this suite removed, so they are not caused by
  it; the machine was under load from a parallel .NET run.
