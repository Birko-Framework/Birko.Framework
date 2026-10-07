---
id: TASK-519
parent: STORY-058
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# ASP.NET Core endpoint mapping for the OAuth server

## Context

The OAuth server has handlers for `/token`, `/device_authorization`, `/authorize` and `/register` but no ASP.NET Core
routes, so DraCode wrote `DraCode.KoboldLair.Server/Auth/OAuthEndpoints.cs` (267 lines): form parsing, HTTP Basic client
authentication, mapping `OAuthServerException` to RFC 6749 error JSON and status codes, approving a device code for the
signed-in user, and gating dynamic registration.

Adopted in the consumer by DraCode TASK-110 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] An extension (e.g. `app.MapOAuthServer(options)`) maps the four endpoints with correct form/Basic parsing and RFC error responses
- [ ] Device-code approval and `/authorize` take the signed-in user from the request principal; dynamic registration can be switched off
- [ ] A hook lets the app take over one grant type (DraCode issues its own service-account tokens — see TASK-520)
- [ ] Tests over a test host: each endpoint's success and error shape

## Out of scope

- Consent UI pages

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
