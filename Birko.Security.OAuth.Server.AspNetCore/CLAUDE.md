# Birko.Security.OAuth.Server.AspNetCore

## Overview
ASP.NET Core minimal-API routes for `Birko.Security.OAuth.Server` (TASK-519, from DraCode's `OAuthEndpoints.cs`). The
server's handlers stay ASP.NET-free; this is the HTTP shell: form and HTTP Basic parsing, RFC 6749 error bodies and
status codes, the signed-in user for `/authorize` and device approval, gated dynamic registration, and a hook that
hands one grant type to the app.

## Project Location
`C:\Source\Birko\Framework\Birko.Security.OAuth.Server.AspNetCore\`

## Components
- `OAuthServerEndpointExtensions.MapOAuthServer(this IEndpointRouteBuilder, Action<OAuthServerEndpointOptions>?)`.
  - **Routes:**
    - `POST /token` and `POST /device_authorization`, which authenticate the client in the request.
    - `POST /device/approve` and `GET /authorize`, which always `RequireAuthorization()` and take the user from the
      principal (`sub`, else NameIdentifier).
    - When `AllowDynamicRegistration` is set, `POST /register` and `POST /register/{clientId}/disable`, which always
      require authorization and add `ConfigureRegistration`'s policy.
  - **Resolved from DI per request:** `OAuthServer`, and `IOAuthClientStore` for the disable endpoint.
  - **RFC behaviour:**
    - Token and device responses carry `Cache-Control: no-store` / `Pragma: no-cache` (§5.1).
    - Errors are `{error, error_description, error_uri}`: 400, except `invalid_client` after HTTP Basic, which is 401
      with `WWW-Authenticate: Basic`; `server_error` is 500 and `temporarily_unavailable` 503.
    - Basic credentials are form-decoded (§2.3.1). A malformed Basic header is `invalid_client`, not ignored.
    - A body `client_secret` beside Basic, or a body `client_id` naming another client, is `invalid_request` (one
      authentication method, §2.3).
  - **`/authorize`:** answers JSON by default (DraCode's SPA shape). `AuthorizeResponseMode.Redirect` sends a 302 with
    `code` and `state` to the registered URI. An error is never redirected.
  - **`GrantHandlers[grant_type]`:** takes a grant over, or adds an extension grant. The framework authenticates nothing
    on that path.
  - **Registration body:** read camelCase with string or numeric `clientType`, since the response names it as a string.
- `OAuthServerEndpointOptions`: paths, `AllowDynamicRegistration` (false), `ConfigureRegistration`,
  `ConfigureUserEndpoints`, `GrantHandlers`, `AuthorizeResponseMode`, `ResolveUserId`.

⚠ The handlers are passed to `MapPost`/`MapGet` typed as `Delegate`. An `HttpContext => Task<IResult>` lambda otherwise
binds the `RequestDelegate` overload, which never writes the returned `IResult`.

## Dependencies
- Birko.Security.OAuth.Server
- Microsoft.AspNetCore.App, consumer-supplied, as for Birko.Security.AspNetCore (TASK-234: a duplicate
  FrameworkReference is NETSDK1087)

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests:
`tests/Birko.Security.OAuth.Server.AspNetCore.Tests` (TestServer over the in-memory stores).
