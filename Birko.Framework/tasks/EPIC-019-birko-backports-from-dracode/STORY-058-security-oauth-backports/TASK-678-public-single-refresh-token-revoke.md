---
id: TASK-678
parent: STORY-058
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-19
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# OAuthServer: revoke one refresh token by its plaintext value (logout)

## Context

DraCode TASK-108 moved its GitHub sign-in sessions onto the OAuth server through `IssueForSubjectAsync` ([[TASK-517]]).
Its `/auth/logout` must end one session, but the server offers no public way to: the stored form of a refresh token
comes from the internal `ClientSecretHasher`, and the only public revoke, `IRefreshTokenStore.RevokeFamilyAsync`, signs
the person out on every device. So DraCode's `FederatedSessions.HashRefreshToken` copies the hex SHA-256 format, and a
DraCode test fails if it changes.

RFC 7009 token revocation is the protocol form of this; an endpoint is not needed for DraCode.

Adopted in the consumer by a DraCode follow-up that deletes `FederatedSessions.HashRefreshToken`
(`Consumers/DraCode/DraCode.KoboldLair.Server/Auth/FederatedSessions.cs`).

## Acceptance criteria

- [ ] A public call on `TokenEndpointHandler` (or `OAuthServer`) revokes the refresh token a caller presents, given the
      client id, without revoking the rest of its family
- [ ] An unknown token, or one belonging to another client, is a no-op (RFC 7009 §2.2: the response does not reveal it)
- [ ] Tests: revoke → that token no longer refreshes, a sibling in the same family still does; another client's token
      is untouched

## Out of scope

- An RFC 7009 `/revoke` endpoint in Birko.Security.OAuth.Server.AspNetCore (file separately if a consumer needs it)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
