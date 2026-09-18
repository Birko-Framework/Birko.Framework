---
id: STORY-020
parent: EPIC-009
status: done
created: 2026-05-28
closed: 2026-05-29
---

# OAuth2 authorization server

## User story

As a developer building an identity service, I want an OAuth2 authorization server in Birko so my apps can issue and validate tokens without third-party dependencies.

## Behaviour

- Token endpoint (all standard grant types + PKCE)
- Authorization endpoint with consent management
- Client registration endpoint
- Persists clients + tokens via `Birko.Data.Stores`
- Composes with the existing `Birko.Communication.OAuth` client side
