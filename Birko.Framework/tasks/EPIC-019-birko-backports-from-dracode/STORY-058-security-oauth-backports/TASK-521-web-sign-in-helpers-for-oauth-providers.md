---
id: TASK-521
parent: STORY-058
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Web (authorization-code) sign-in helpers for external OAuth providers, GitHub first

## Context

`Birko.Communication.OAuth.Providers` covers GitHub's device flow only. DraCode's GitHub web sign-in needed a short-lived
single-use `state` store (`OAuthStateStore`, 51 lines), a DI-friendly code exchanger over `OAuthClient`
(`GitHubTokenExchanger`, 57) and a user-info client returning id/login/name/email (`GitHubUserInfoClient`, 62). None of it is
DraCode-specific.

Adopted in the consumer by DraCode TASK-111 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] A state store (create, single-use consume, expiry) in `Birko.Communication.OAuth`
- [x] GitHub provider: authorization-code settings and a user-info call returning a typed profile
- [x] An injectable exchanger interface, so apps can test callbacks without github.com
- [x] Tests

## Out of scope

- An app's allowlist and user provisioning (policy, stays in apps)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Outcome (2026-10-18)

- **What shipped.** In `Birko.Communication.OAuth`:
  - `IOAuthStateStore` / `InMemoryOAuthStateStore`, with `CreateAsync(binding, data)` and `TryConsumeAsync(state, binding)`.
    A state is single-use (atomic `TryRemove`) and expires on an injectable `TimeProvider` (default 10 min). The store
    is bounded by `MaxPending` (default 10 000: expired states are swept first, then the oldest is evicted). The browser
    binding is stored hashed and compared in constant time, and a state is spent by its first presentation, even a
    refused one.
  - `IOAuthCodeExchanger` / `OAuthCodeExchanger`, with a fresh `OAuthClient` per call, so no user's token stays cached.

  In `Birko.Communication.OAuth.Providers`: `GitHubOAuthProvider.AuthorizationEndpoint` and `CreateWebFlowSettings`, and
  `IGitHubUserClient` / `GitHubUserClient`.
  - `GetUserAsync` returns `GitHubUser(Id, Login, Name, Email)`, and `GetPrimaryEmailAsync` the verified primary email.
  - Headers are set per request, so a shared HttpClient is never modified, and `apiBase` covers GitHub Enterprise Server.
  - A refusal is an `OAuthException`, and a response without a numeric id is refused.
- **Defects fixed in `OAuthClient`, red on HEAD (4 of 5; the per-request timeout test is a pin):**
  - A token error that came back with HTTP 200 was parsed as a token and threw `KeyNotFoundException`. GitHub answers
    `bad_verification_code` that way, so a bad or reused code would have been a 500 at DraCode's callback.
  - GitHub device-flow polling broke on the first poll: `authorization_pending` comes with 200. This is the existing
    device flow Birko.AI.Providers uses for Copilot sign-in. Polling now continues.
  - Token requests sent no `Accept: application/json`, so GitHub answered form-encoded, which the parser could not read.
  - `Timeout` was set on a borrowed HttpClient. That throws once the client has sent a request, and it silently changed
    the caller's timeout; a borrowed client now gets the timeout per request.
- **DraCode's own defect.** Its state was not bound to the browser, which allows login CSRF. The library adds the
  binding, and a test shows another browser's state is refused.
- **Tests.**
  - `Task521OAuthClientTests` 5.
  - `OAuthStateStoreTests` 18, with mutations: a consume that does not remove fails 4, and no binding check fails 3.
  - `GitHubWebSignInTests` 21, including an end-to-end sign-in against fakes.
- **Regression.** OAuth 74, OAuth.Providers 21, AI.Agents 70, AI.Providers 246, AI 75. Sandbox builds.
- **Docs.** The CLAUDE.md and README of both projects, and `docs/communication.md`. Communication is out of the spec map.
- **Adoption (DraCode TASK-111).** DraCode's `Issue()` / `TryConsume(state)` become `CreateAsync(binding, …)` /
  `TryConsumeAsync(state, binding)`, and it needs a binding cookie.
