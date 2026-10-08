---
id: TASK-536
parent: STORY-061
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `MapWebSocketEndpoint(requireAuthentication: true)` accepts anonymous upgrades when no auth service is registered

## Context

A Birko defect, found while reviewing DraCode. `WebSocketAuthenticationMiddleware`
(`Birko.Communication.WebSocket/Middleware/WebSocketMiddleware.cs:101–102`) and the legacy `MapWebSocket`
(`WebSocketEndpointExtensions.cs:77–78`) both do `GetService<WebSocketAuthenticationService>()` and then
`if (authService != null) { … }` — and otherwise let the request through. `requireAuthentication` defaults to `true`,
and nothing in the framework registers that service; only tests construct it. So the default is a safety flag that is a
silent no-op (rule 51).

The consumers route around it rather than through it: Symbio's `Program.cs` (~L458) passes
`requireAuthentication: false` plus `.RequireAuthorization()`, commenting that the flag "can't validate per-user JWTs".
DraCode's `Program.cs` (L1071–1140) doesn't use Birko's mapping at all — three copy-pasted `app.Map` blocks and a
hand-written claims parser.

Adopted in the consumer by DraCode TASK-134 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] With `requireAuthentication: true` and no auth service registered, the endpoint refuses: a startup / mapping-time exception, or 401 on every upgrade — decide and document
- [ ] The flag's name and docs say what it actually is (the static-token mechanism), or it is replaced
- [ ] The per-user path — `.RequireAuthorization()` + `ICurrentUser` — is documented, or has a helper
- [ ] Regression test: no service registered + default flag → anonymous upgrade rejected; proven red on the current code
- [ ] If this changes behaviour for a consumer, a CHANGELOG entry per CLAUDE-maintenance.md § Breaking changes

## Human test plan

N/A — covered by tests.

## Implementation plan
