---
id: TASK-519
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

# ASP.NET Core endpoint mapping for the OAuth server

## Context

The OAuth server has handlers for `/token`, `/device_authorization`, `/authorize` and `/register` but no ASP.NET Core
routes, so DraCode wrote `DraCode.KoboldLair.Server/Auth/OAuthEndpoints.cs` (267 lines): form parsing, HTTP Basic client
authentication, mapping `OAuthServerException` to RFC 6749 error JSON and status codes, approving a device code for the
signed-in user, and gating dynamic registration.

Adopted in the consumer by DraCode TASK-110 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] An extension (e.g. `app.MapOAuthServer(options)`) maps the four endpoints with correct form/Basic parsing and RFC error responses
- [x] Device-code approval and `/authorize` take the signed-in user from the request principal; dynamic registration can be switched off
- [x] A hook lets the app take over one grant type (DraCode issues its own service-account tokens — see TASK-520)
- [x] Tests over a test host: each endpoint's success and error shape

## Out of scope

- Consent UI pages

## Human test plan

N/A — library behaviour, covered by unit tests.

## Outcome (2026-10-18)

New project **`Birko.Security.OAuth.Server.AspNetCore`**, from DraCode's `OAuthEndpoints.cs`.

- **Routes.** `app.MapOAuthServer(o => …)` maps:
  - `POST /token` and `POST /device_authorization`, which authenticate the client from form fields or HTTP Basic.
  - `POST /device/approve` and `GET /authorize`, which always require an authenticated user, taken from the principal
    (`sub`, else NameIdentifier).
  - `POST /register` and `/register/{clientId}/disable`, opt-in and always behind authorization plus
    `ConfigureRegistration`'s policy.
- **Taking over a grant.** `GrantHandlers[grant_type]` lets the app take over a grant or add an extension grant.
- **`/authorize` response.** `AuthorizeResponseMode` is Json (DraCode's shape, the default) or Redirect (302 with `code`
  and `state`); an error is never redirected.

**Defects fixed against DraCode.** Each is pinned by a test that fails with DraCode's behaviour put back.
- **Response contract.** No `Cache-Control: no-store` / `Pragma: no-cache` (RFC 6749 §5.1). `invalid_client` after HTTP
  Basic was 400, and is now 401 with `WWW-Authenticate: Basic` (§5.2); `server_error` is 500, `temporarily_unavailable` 503.
- **HTTP Basic.**
  - Credentials were decoded with `Uri.UnescapeDataString`, so a `+` was not a space (§2.3.1).
  - A malformed Basic header was silently ignored in favour of the body credentials; it is now `invalid_client`.
  - Basic plus body credentials naming another client, or a body secret, was resolved by picking one; it is now
    `invalid_request` (§2.3).
- **Registration body.** Plain minimal-API binding refused the `client_type` string (`"Confidential"`) that the
  registration response itself returns (measured 400). The body is now read with string or numeric enums.
- **Found on the way.** A `HttpContext => Task<IResult>` lambda binds MapPost's `RequestDelegate` overload, which never
  writes the returned result. The handlers are passed as `Delegate`; the project CLAUDE.md records why.

**Tests and checks.**
- Tests: 13 over TestServer. The API is new, so instead of a red check on HEAD: putting back the four DraCode behaviours
  (no no-store, 400 for `invalid_client`, `UnescapeDataString`, ignoring a malformed Basic header) fails 6 of 13.
- Builds: the Sandbox builds, with the import committed in the Sandbox repo.
- Docs: `security.md` § ASP.NET Core endpoints, the project CLAUDE.md and README, and the spec map's out-of-scope list.
  The OAuth server is not in a spec area.

**Not done:** a consent-POST route. A consent UI is out of scope.
