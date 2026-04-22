# Birko.Communication.REST.Server

Lightweight REST server built on `HttpListener` with route registration, middleware pipeline, and optional API-key/Bearer authentication.

## Project Location
`C:\Source\Birko.Communication.REST.Server\` — shared project (.shproj)

## Components

- **RestServer.cs** — `HttpListener`-based REST server with async request processing, route registration (GET/POST/PUT/DELETE/PATCH), parameterized path matching (`{id}`), and a composable middleware pipeline. Includes `RestRequest`, `RestResponse`, and `RestRequestContext` types.
- **Middleware/RestAuthenticationConfiguration.cs** — REST-specific authentication settings: API key header, Authorization header, query-string token support. Extends `AuthenticationConfiguration`.
- **Middleware/RestAuthenticationMiddleware.cs** — Factory that creates a middleware function validating tokens via `RestAuthenticationService` before passing requests to the next handler.
- **Middleware/RestAuthenticationService.cs** — Adapter that extracts authentication tokens from REST requests (Bearer header, API key header, or query parameter) and validates them using `AuthenticationService` from `Birko.Security.Authentication`.

## Dependencies

- Birko.Security (AuthenticationConfiguration, AuthenticationService)
- Microsoft.Extensions.Logging (ILogger)
- System.Net.Http (HttpListener)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
