---
id: TASK-520
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

# client_credentials: let the app shape the token; read space-delimited scopes as permissions

## Context

DraCode replaces the server's `client_credentials` handling with its own `ServiceAccountTokenIssuer` (97 lines) only to
set `sub = "service:<name>"` and a permission `scope` its pipeline enforces. Separately, `ClaimsCurrentUser` splits the
permission claim on commas, so a standard space-delimited OAuth `scope` ("a b") reads as one permission — DraCode records it
as a known limitation in its `Program.cs`.

Adopted in the consumer by DraCode TASK-110 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] The `client_credentials` grant accepts an app-supplied subject/claims hook, so a consumer need not replace the grant
- [x] `ClaimsCurrentUser` (or `ClaimMappingOptions`) reads a space-delimited `scope` claim as separate permissions; comma-delimited still works
- [x] Tests for both

## Out of scope

- Changing other grants

## Human test plan

N/A — library behaviour, covered by unit tests.

## Outcome (2026-10-18)

- **Hook.** `TokenEndpointHandler.ShapeClientCredentialsToken` (new `ClientCredentialsTokenContext`) runs after the
  client is authenticated and its scope granted. It sets `Subject` (e.g. `service:billing`) and extra `Claims`; the
  scope stays the grant's. A registered claim or an empty subject throws `InvalidOperationException` rather than being
  dropped. DraCode's `ServiceAccountTokenIssuer` (a replacement grant) is no longer needed.
- **Permissions.** New `ClaimMappingOptions.SplitPermissionsOnSpace` (`bool?`). When null, the default, a permission
  claim is split on spaces only when `PermissionClaim == "scope"`, since RFC 6749 §3.3 forbids spaces inside a scope
  token. Commas and semicolons still split.
  - No consumer decision was needed: the only consumer with `PermissionClaim = "scope"` is DraCode (grep across
    Consumers), and this is its recorded limitation.
- **Tests.** `ClientCredentialsShapeTests` (7) and `ScopePermissionTests` (4). Red on HEAD, with the new members stubbed
  as ignored: 5 of 7 and 2 of 4.
  - Pins: the default subject; the hook not running for a bad secret; comma joins still splitting; other claims not
    split on spaces.
- **Regression.** Every importer is green: Security.AspNetCore 117, Grants 184, OAuth.Server 68, OAuth.Server.InMemory
  14, OAuth.Server.SqLite 20, OAuth.Server.AspNetCore 13. Sandbox builds.
- **Spec and docs.** Spec `security-and-authorization`: the claim-splitting requirement and two scenarios. Docs:
  `security.md` § Shaping a client_credentials token, and the CLAUDE.md of both projects.
