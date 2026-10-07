---
id: TASK-517
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

# OAuthServer: issue a token pair for a subject that signed in elsewhere (federated sign-in)

## Context

DraCode signs people in through GitHub and then needs its *own* access + refresh tokens for them. The OAuth server's
`refresh_token` grant already does everything DraCode re-implemented in an in-memory `RefreshTokenStore` (hashed records,
rotation, reuse detection, persistence through `IRefreshTokenStore`), but there is no public way to start that family for a
user who authenticated outside the server: `TokenEndpointHandler.IssueTokenPairAsync(clientId, userId, scope)` is private.
So DraCode kept its own store, and its GitHub sessions are lost on every restart (DraCode TASK-108).

Adopted in the consumer by DraCode TASK-108 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] A public API on `OAuthServer` (or `TokenEndpointHandler`) issues an access + refresh token pair for a given client, subject and scope, as if an authorization had just completed — the refresh token then renews through the normal `refresh_token` grant
- [ ] Extra claims for the access token can be supplied (DraCode puts `name`, `roles` and a permission `scope` in its tokens)
- [ ] The subject is a string (`github:42`), not only a Guid
- [ ] Tests: issue → refresh rotates → a reused refresh token revokes the family

## Out of scope

- Changing the existing grants
- Federation clients themselves (TASK-521)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
