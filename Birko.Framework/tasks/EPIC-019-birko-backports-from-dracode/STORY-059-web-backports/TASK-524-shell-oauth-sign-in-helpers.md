---
id: TASK-524
parent: STORY-059
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

# Birko.Web.Shell auth: OAuth redirect parsing, token expiry and scheduled refresh

## Context

`Birko.Web.Shell/src/auth` has `createAuthStore` and guards but nothing for an OAuth redirect sign-in. DraCode TASK-056
plans app-side code for it: read `#access_token=…&refresh_token=…` from the URL fragment after a provider redirect, decode
the JWT `exp`, and refresh shortly before expiry through `ApiClient`'s existing `onRefreshToken` / `onUnauthorized` hooks.
None of that is app-specific.

Consumer side: DraCode `EPIC-018 / STORY-039` (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] A helper parses (and then clears from the address bar) an auth fragment into the auth store
- [ ] Token expiry is read from the JWT `exp`; a scheduler refreshes ahead of expiry and on a 401, through a pluggable refresh call
- [ ] The auth store's token is what `ApiClient`, `WsClient` and `SseClient` send
- [ ] Tests for parsing, expiry and the refresh schedule

## Out of scope

- A login page or provider buttons (apps)

## Human test plan

N/A — covered by unit tests.

## Implementation plan
