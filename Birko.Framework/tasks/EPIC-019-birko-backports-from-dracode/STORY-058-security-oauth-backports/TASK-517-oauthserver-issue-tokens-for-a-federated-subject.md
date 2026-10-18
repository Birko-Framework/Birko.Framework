---
id: TASK-517
parent: STORY-058
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

# OAuthServer: issue a token pair for a subject that signed in elsewhere (federated sign-in)

## Context

DraCode signs people in through GitHub and then needs its *own* access + refresh tokens for them. The OAuth server's
`refresh_token` grant already does everything DraCode re-implemented in an in-memory `RefreshTokenStore` (hashed records,
rotation, reuse detection, persistence through `IRefreshTokenStore`), but there is no public way to start that family for a
user who authenticated outside the server: `TokenEndpointHandler.IssueTokenPairAsync(clientId, userId, scope)` is private.
So DraCode kept its own store, and its GitHub sessions are lost on every restart (DraCode TASK-108).

Adopted in the consumer by DraCode TASK-108 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] A public API on `OAuthServer` (or `TokenEndpointHandler`) issues an access + refresh token pair for a given client, subject and scope, as if an authorization had just completed — the refresh token then renews through the normal `refresh_token` grant
- [x] Extra claims for the access token can be supplied (DraCode puts `name`, `roles` and a permission `scope` in its tokens)
- [x] The subject is a string (`github:42`), not only a Guid
- [x] Tests: issue → refresh rotates → a reused refresh token revokes the family

## Out of scope

- Changing the existing grants
- Federation clients themselves (TASK-521)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Outcome (2026-10-18)

`TokenEndpointHandler.IssueForSubjectAsync(clientId, subject, scope, claims)` issues an access + refresh pair for a
string subject, as if an authorization had just completed. The refresh token then works through the ordinary
`refresh_token` grant.

- **Claims.** The extra claims are stored on the new `RefreshTokenRecord.Claims` and re-issued on every refresh: with
  rotation they move to the rotated record, and without rotation they apply to the new access token. A registered claim
  (`sub`, `client_id`, `iss`, `scope`, `aud`, `exp`, `iat`, `nbf`, `jti`) is refused; DraCode's permission `scope` maps
  onto the OAuth scope.
- **Refusals.** An unknown or disabled client gives `invalid_client`. A client without the `refresh_token` grant gives
  `unauthorized_client`, since its refresh token would be unusable. A scope outside the client's gives `invalid_scope`.
  No secret is checked: the host asserts the sign-in.
- **Tests.** `FederatedIssueTests`, 12 cases: issue, refresh rotating with the claims kept (twice), no rotation, reuse
  revoking the family, refusals. The API is new, so it cannot be red on HEAD. Mutation instead: with the refresh path
  not passing `record.Claims`, 2 fail.
- **Regression.** Birko.Security.OAuth.Server.Tests 60/60 (the only importer); Sandbox builds.
- **Not verified.** DraCode's server does not build at present, for an unrelated reason: its `SqlUsageRepository` still
  implements the pre-TASK-530 `IUsageRepository`. DraCode's own stores must persist `Claims` once it adopts this
  (DraCode TASK-108, and [[TASK-518]] ships stores that do).
- **Docs.** `docs/security.md` and the project CLAUDE.md, § Federated sign-in. The OAuth server is out of the spec map's
  scope.
