---
id: TASK-520
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

# client_credentials: let the app shape the token; read space-delimited scopes as permissions

## Context

DraCode replaces the server's `client_credentials` handling with its own `ServiceAccountTokenIssuer` (97 lines) only to
set `sub = "service:<name>"` and a permission `scope` its pipeline enforces. Separately, `ClaimsCurrentUser` splits the
permission claim on commas, so a standard space-delimited OAuth `scope` ("a b") reads as one permission — DraCode records it
as a known limitation in its `Program.cs`.

Adopted in the consumer by DraCode TASK-110 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] The `client_credentials` grant accepts an app-supplied subject/claims hook, so a consumer need not replace the grant
- [ ] `ClaimsCurrentUser` (or `ClaimMappingOptions`) reads a space-delimited `scope` claim as separate permissions; comma-delimited still works
- [ ] Tests for both

## Out of scope

- Changing other grants

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
