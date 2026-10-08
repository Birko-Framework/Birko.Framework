---
id: TASK-536
parent: STORY-061
feature: null
status: done
picked-by: fix-next
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

- [x] With `requireAuthentication: true` and no auth service registered, the endpoint refuses: a startup / mapping-time exception, or 401 on every upgrade — decide and document
- [x] The flag's name and docs say what it actually is (the static-token mechanism), or it is replaced
- [x] The per-user path — `.RequireAuthorization()` + `ICurrentUser` — is documented, or has a helper
- [x] Regression test: no service registered + default flag → anonymous upgrade rejected; proven red on the current code
- [x] If this changes behaviour for a consumer, a CHANGELOG entry per CLAUDE-maintenance.md § Breaking changes

## Human test plan

N/A — covered by tests.

## Implementation plan

## Progress log

- step 2 — picked at the user's request (2026-10-08), after TASK-200 moved to Symbio; ranked above TASK-537 because it is an authentication bypass on the default argument, reachable by any anonymous client
- step 3 — verified: holds. `WebSocketMiddleware.cs:101-102` and `WebSocketEndpointExtensions.cs:77-78` skip the check when `WebSocketAuthenticationService` is not registered; nothing in the framework registers it. Consumer usage measured: gameshow-app uses `MapWebSocketEndpointNoAuth`, Symbio passes `requireAuthentication: false`, DraCode does not use the mapping — so failing closed breaks no known consumer
- step 4 — layer: local (the framework is the defect's home)
- step 5 — fix in `Birko.Communication.WebSocket/Middleware/WebSocketAuthenticationGate.cs` (new; the one check both paths call), `WebSocketMiddleware.cs`, `WebSocketEndpointExtensions.cs`; tests in `tests/Birko.Communication.WebSocket.Tests/WebSocketEndpointAuthenticationTests.cs`; suite 43/43 green, no new nullable warnings
- step 6 — reverted fix (stashed the two middleware files, gate file left in place unused): 4/10 new tests failed; fix-dependent = Middleware_RequireAuth_NoServiceRegistered_Rejects401, MapWebSocketEndpoint_RequireAuth_NoServiceRegistered_Throws, LegacyMapWebSocket_RequireAuth_NoServiceRegistered_Throws, LegacyMapWebSocket_RequireAuth_ServiceRemovedAfterMapping_Rejects401; contract pins = Middleware_RequireAuth_InvalidToken_Rejects401, Middleware_RequireAuth_ValidToken_Passes, Middleware_AuthNotRequired_NoServiceRegistered_Passes, MapWebSocketEndpoint_RequireAuth_ServiceRegistered_Maps, MapWebSocketEndpointNoAuth_NoServiceRegistered_Maps, LegacyMapWebSocket_RequireAuth_InvalidToken_Rejects401
- step 7 — no spec area covers Birko.Communication.WebSocket (unspecced project, tracked by TASK-226); nothing to regenerate. Docs: project README (new "Mapping endpoints and authentication"), CHANGELOG 2026-10-08 entry with migration, CLAUDE.md Recent Updates (9.4 KB, under budget)
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log

## Outcome

**What was fixed.** `MapWebSocketEndpoint(…, requireAuthentication: true)` — the default — and the legacy
`MapWebSocket` ran the static-token check only when `WebSocketAuthenticationService` was registered. Nothing registers
it, so by default anyone could open the socket. Both paths now call one gate (`WebSocketAuthenticationGate`) that fails
closed: mapping throws `InvalidOperationException` (naming the endpoint and the two remedies) when the container reports
the service missing, and a request that still reaches the gate is refused with 401 before the handler runs.

**Proof.** Reverting the two middleware files fails 4 of the 10 new tests — exactly the missing-service cases, at map
time and per request on both paths. The other 6 pass either way and are contract pins (wrong token → 401, right token →
through, flag off → through, registered service maps, `NoAuth` maps), not evidence of the fix.

**Judgement calls.**
- *Throw at map time and 401 per request, not one or the other.* Startup failure is the loud signal (rule 49 —
  an explicit declaration that cannot be honoured throws); the 401 stays as the backstop for a container that cannot
  answer `IServiceProviderIsService`, or a request scope that differs from the root. 401-only would let a misconfigured
  app run looking healthy.
- *The flag was documented, not renamed.* Renaming `requireAuthentication` would break every caller to fix a word;
  the XML doc and README now say it means static/M2M tokens and point per-user auth at `.RequireAuthorization()`.
  No new helper for the per-user path — `requireAuthentication: false` + `.RequireAuthorization()` is already one line
  (Symbio's shape), and a helper would add a second way to say it.
- *Breaking for an app that relied on the open default.* Measured: no consumer does (gameshow-app `NoAuth`, Symbio
  `false` + `.RequireAuthorization()`, DraCode unmapped). CHANGELOG carries the migration anyway.

**Flagged, not fixed.** Nothing new. The send-concurrency bug in `WebSocketServer.BroadcastAsync` is TASK-537's.
