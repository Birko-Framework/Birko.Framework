---
id: TASK-027
parent: STORY-020
status: todo
priority: P2
assignee: ai
created: 2026-05-28
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

- [ ] `Birko.Security.OAuth.Server` shared project exists, registered everywhere
- [ ] Token endpoint (client credentials, authorization code + PKCE, refresh token, device code)
- [ ] Authorization endpoint with consent management
- [ ] Client registration endpoint
- [ ] Models persist via `Birko.Data.Stores` (provider-agnostic)
- [ ] DI extensions wire endpoints + storage
- [ ] xUnit tests covering each grant type
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- OpenID Connect (could be a separate `Birko.Security.OIDC.Server`)
- SAML 2.0
