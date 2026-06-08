---
id: TASK-027
parent: STORY-020
status: done
priority: P2
assignee: ai
created: 2026-05-28
closed: 2026-05-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Security.OAuth.Server

## Context

OAuth2 authorization server. The client side already exists in `Birko.Communication.OAuth`. Persists clients + tokens via `Birko.Data.Stores`.

## Acceptance criteria

- [x] `Birko.Security.OAuth.Server` shared project exists, registered everywhere
- [x] Token endpoint (client credentials, authorization code + PKCE, refresh token, device code)
- [x] Authorization endpoint with consent management
- [x] Client registration endpoint
- [x] Models persist via `Birko.Data.Stores` (provider-agnostic)
- [x] DI extensions wire endpoints + storage (`OAuthServer` composition root — host registers as singleton)
- [x] xUnit tests covering each grant type (43 tests, all passing)
- [x] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- OpenID Connect (could be a separate `Birko.Security.OIDC.Server`)
- SAML 2.0
