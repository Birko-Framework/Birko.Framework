---
id: TASK-829
parent: EPIC-020
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# One trusted-proxy list for forwarded headers: ASP.NET, Birko.Security and the loopback bypass agree

From a discussion with the user (2026-11-07) about whether Birko should ship its own reverse proxy. The answer was no:
the edge stays Nginx Proxy Manager / Traefik / Caddy, and an app that needs dynamic routes embeds YARP. Birko should
instead own the piece every app behind a proxy needs: deciding who the client is.

## Context (surveyed 2026-11-07, not measured)

Every deployment runs behind a proxy (godzilla: Cloudflare → Nginx Proxy Manager → app). "Who is the client, over which
scheme, on which host" is decided in several unconnected places:

- **Birko.Security:** `AuthenticationConfiguration.TrustedProxies` (address or CIDR) is used only for IP-bound tokens.
  `AuthenticationService.GetClientIpAddress` walks `X-Forwarded-For` from the right, skipping trusted proxies (SH-M249).
- **Each consumer wires ASP.NET's `ForwardedHeaders` itself:**
  - **Presenter** (`Presenter.Host/Hosting/ForwardedHeadersSetup.cs`, TASK-048/056): its own `TrustedProxyOptions` with
    `KnownProxies` / `KnownNetworks` / `ForwardLimit` / `AllowedHosts`. It clears ASP.NET's loopback default, skips the
    middleware when nothing is configured, forwards the host only against a list, and throws on a malformed entry.
  - **Symbio** (`Symbio.Infrastructure/Http/SymbioRateLimiting.cs`, `UseSymbioForwardedHeaders`):
    `Symbio:RateLimit:TrustForwardedHeaders` plus `KnownProxies` / `KnownNetworks`, a separate option set with the same
    intent. Its proxy addresses are configured again per stack on godzilla (`symbio/compose.yml`,
    `flowerfurstudio/compose.yml` `Storefront__ForwardedHeaders__KnownProxies__0`).
  - **Affiliate** (`Affiliate/Startup.cs:88-93`): `KnownNetworks.Clear(); KnownProxies.Clear();` with `XForwardedFor |
    XForwardedProto` and nothing added, which trusts **every** sender's `X-Forwarded-For` / `-Proto`. **Possible
    spoofing finding, from reading the code, not measured:** any caller can choose the client address and the scheme the
    app sees. Affiliate is moving into Symbio, so this may die with it; verify before acting.
- **`LoopbackBypassMiddleware`** (Birko.Security.AspNetCore) is unsafe behind a same-machine proxy, which forwards
  remote callers over loopback. That is only a warning in `docs/security.md`; nothing enforces it.
- **`SubdomainTenantResolver`** reads `Request.Host`, so behind a proxy it is right only if forwarded headers ran first
  with the host trusted, or the proxy passes `Host` through. Not documented.

So the trusted-proxy list lives in two to four places per app and can disagree. An IP-bound token check and the rate
limiter can then see different client addresses for the same request.

## Acceptance criteria

- [ ] **One registration** (e.g. `AddBirkoForwardedHeaders(o => …)` / `UseBirkoForwardedHeaders()`) with known proxies
      and networks, forward limit, and allowed hosts (host forwarding is off unless hosts are listed). With nothing
      configured, nothing is trusted and ASP.NET's loopback default is cleared, as Presenter does. A malformed entry
      throws at startup.
- [ ] **The same list feeds Birko.Security's `TrustedProxies`**, so the token IP binding and `HttpContext.Connection
      .RemoteIpAddress` agree. A test shows one request through a trusted proxy yields one client address on both.
- [ ] **The loopback bypass refuses to start when trusted proxies are configured.** A host refusing to start is
      already how the bypass reports a missing middleware.
- [ ] **Ordering is documented and, where possible, checked:** forwarded headers before authentication, the rate
      limiter, the loopback bypass and the tenant resolvers.
- [ ] **Consumers:** a follow-up note per consumer (Presenter, Symbio, Affiliate) to replace its copy. Consumer files
      are not edited from here.
- [ ] Decide with the user: fold Presenter's `TrustedProxyOptions` shape in as the framework's (it is the most complete),
      or a new options type.

## Human test plan

On godzilla, behind Nginx Proxy Manager: with the proxy's address configured, a request shows the real client IP and
`https` (scheme) in the app's log or diagnostics endpoint. With it not configured, the app sees the proxy's address and
a spoofed `X-Forwarded-For: 1.2.3.4` sent by a client is ignored.
