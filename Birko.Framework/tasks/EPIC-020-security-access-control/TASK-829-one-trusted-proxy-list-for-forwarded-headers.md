---
id: TASK-829
parent: EPIC-020
feature: null
status: done
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

- [x] **One registration** (e.g. `AddBirkoForwardedHeaders(o => …)` / `UseBirkoForwardedHeaders()`) with known proxies
      and networks, forward limit, and allowed hosts (host forwarding is off unless hosts are listed). With nothing
      configured, nothing is trusted and ASP.NET's loopback default is cleared, as Presenter does. A malformed entry
      throws at startup.
      → `AddBirkoForwardedHeaders(section | Action<>)`, with no `Use…` (grill Q2). Pinned by `Task829ForwardedHeadersTests`
      (Security.AspNetCore): nothing configured, host forwarding, and the refusals.
- [x] **The same list feeds Birko.Security's `TrustedProxies`**, so the token IP binding and `HttpContext.Connection
      .RemoteIpAddress` agree. A test shows one request through a trusted proxy yields one client address on both.
      → `Through_a_trusted_proxy_the_connection_and_the_token_binding_see_one_client` and
      `The_token_binding_does_not_read_the_headers_a_second_time`. The latter is red with the short-circuit disabled
      (measured).
- [x] **The loopback bypass refuses to start when trusted proxies are configured.** A host refusing to start is
      already how the bypass reports a missing middleware.
      → `The_loopback_bypass_refuses_to_start_behind_a_trusted_proxy_in_either_registration_order`, red with the check
      disabled.
- [x] **Ordering is documented and, where possible, checked:** forwarded headers before authentication, the rate
      limiter, the loopback bypass and the tenant resolvers.
      → Enforced by construction (a startup filter at index 0), pinned by
      `The_headers_are_applied_before_the_apps_first_middleware`, and documented in `docs/security.md` § Behind a reverse
      proxy and on `SubdomainTenantResolver` / `LoopbackBypass`.
- [x] **Consumers:** a follow-up note per consumer (Presenter, Symbio, Affiliate) to replace its copy. Consumer files
      are not edited from here.
      → § Consumer follow-ups below.
- [x] Decide with the user: fold Presenter's `TrustedProxyOptions` shape in as the framework's (it is the most complete),
      or a new options type. **Decided 2026-11-07 (the user): a new type, fed from `AuthenticationConfiguration`.**
      Its `TrustedProxies` list (addresses and CIDR) is the one source, and ASP.NET's options are derived from it.

## Out of scope

- Consumer files (Presenter, Symbio, Affiliate): each replaces its own copy, per § Consumer follow-ups.
- Strict parsing of the transport configurations' own `TrustedProxies` (REST, SOAP, SSE, WebSocket) → TASK-830.

## Human test plan

Locally, behind a real nginx container. This replaces Q10's Birko.Sandbox, which is a console harness with no web host;
the user chose a throwaway web app, 2026-11-07.

**Harness.**
- A `Microsoft.NET.Sdk.Web` project importing the same projitems as `tests/Birko.Security.AspNetCore.Tests`, through
  `$(BirkoSrc)`.
- Its `Program.cs` calls `AddBirkoSecurity`, with `LoopbackBypass` read from env `LOOPBACK_BYPASS`, then
  `AddBirkoForwardedHeaders(builder.Configuration.GetSection("Birko:ForwardedHeaders"))` and
  `AddSingleton<WebSocketAuthenticationService>()`.
- `GET /who` prints `RemoteIpAddress`, `Request.Scheme`, `Request.Host` and `WebSocketAuthenticationService.GetClientIpAddress`.
- Run it on `http://0.0.0.0:5829`. In front of it, `nginx` in Docker on port 8443 with TLS, proxying to
  `host.docker.internal:5829` with `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;` and
  `X-Forwarded-Proto $scheme`.

**Steps.**
- [x] Set `Birko__ForwardedHeaders__TrustedProxies__0` to the address nginx connects from (run step 2 first to read
      it). `curl -k https://localhost:8443/who` shows your machine's address as the client, the same address on the
      token-binding line, and `https`.
  - **Passed 2026-11-07:** run by Claude at the user's request, nginx:alpine (1.31.8) in Docker Desktop.
    - Docker Desktop delivers nginx's connection to the host over **loopback**, so the proxy to trust was `127.0.0.1`,
      not the docker gateway.
    - The client (nginx's `$remote_addr`) was `172.17.0.1` on both lines, with `https`.
    - A spoofed `X-Forwarded-For: 1.2.3.4` was left unread, and the client stayed `172.17.0.1` (forward limit 1).
- [x] With the variable unset, the same request shows nginx's address and `http`. Adding
      `-H "X-Forwarded-For: 1.2.3.4"` changes nothing.
  - **Passed 2026-11-07:** `127.0.0.1` / `http` on both lines, with or without the spoofed header.
- [x] With the variable set and `LOOPBACK_BYPASS=true`, the app refuses to start with
      `BirkoSecurityOptions.LoopbackBypass is true, but Birko:ForwardedHeaders:TrustedProxies lists …`.
  - **Passed 2026-11-07:** it refused with `… lists 127.0.0.1, so this host runs behind a reverse proxy …`. This
    is the very case it guards against: a Docker Desktop proxy reaches the app over loopback.

Run headless on 2026-11-07, without nginx (the client on 127.0.0.1 standing in for the proxy):
- trusted `127.0.0.1` gives `1.2.3.4` / `https` on both lines
- nothing trusted gives `127.0.0.1` / `http`
- bypass plus a proxy refuses to start

That run is not the plan above, which still needs a real proxy in front.

The same check on godzilla, behind Nginx Proxy Manager, belongs to the Symbio follow-up.

## Consumer follow-ups

Consumer files are not edited from here. Each consumer replaces its own copy with
`builder.Services.AddBirkoForwardedHeaders(builder.Configuration.GetSection("Birko:ForwardedHeaders"))` and deletes its
`UseForwardedHeaders` call; there is no `Use…` to add. Found by reading the code on 2026-11-07, not run.

- **Presenter:** delete `Presenter.Host/Hosting/ForwardedHeadersSetup.cs` and `TrustedProxyOptions.cs`.
  - The config moves from `Presenter:ForwardedHeaders` to `Birko:ForwardedHeaders`, and `KnownProxies` plus
    `KnownNetworks` merge into one `TrustedProxies` list. `AllowedHosts` and `ForwardLimit` keep their names.
  - The pairing QR codes keep reading the forwarded scheme and host, as long as `AllowedHosts` lists them.
- **Symbio:**
  - Replace `UseSymbioForwardedHeaders` in `Symbio.Infrastructure/Http/SymbioRateLimiting.cs` and the
    `Symbio:RateLimit:TrustForwardedHeaders` / `KnownProxies` / `KnownNetworks` options.
  - On godzilla, the per-stack compose variables (`symbio/compose.yml`, and `flowerfurstudio/compose.yml`
    `Storefront__ForwardedHeaders__KnownProxies__0`) become `Birko__ForwardedHeaders__TrustedProxies__0`.
  - Then run this task's human test there, behind Nginx Proxy Manager.
- **Affiliate:** `Affiliate/Startup.cs:88-93` and `:126` clear both of ASP.NET's lists, which ASP.NET reads as
  *trust every sender*: any caller chooses the client address and the scheme. That is read from code, not measured.
  Measure it first; it may disappear when Affiliate moves into Symbio. Registering `AddBirkoForwardedHeaders` fixes it
  even with the old `UseForwardedHeaders()` call left in place, because its `PostConfigure` wins (pinned by
  `Nothing_configured_ignores_a_spoofed_header_even_where_something_else_switched_the_headers_on`).

## Implementation plan

> Drafted 2026-11-07 by the Plan agent, after the user's decision on the last criterion.

### Grill outcome (2026-11-07, the user agreed every recommendation) — overrides the steps below where they differ
- **Q1, the one list:** `AuthenticationConfiguration` is abstract (one subclass per transport, several per app, none in
  JWT-only apps). So the list is a standalone concrete **`TrustedProxySettings`** in Birko.Security, in the same format
  and parsed by `TrustedProxyList`, bound once and read by `AddBirkoForwardedHeaders`. Transport configs keep their own
  lists for `HttpListener` hosts. There is no `<TConfig>` overload.
- **Q2, pipeline placement:** the front-inserting `IStartupFilter` is the only mechanism; there is **no `UseBirkoForwardedHeaders()`**.
- **Q3, fallback headers:** behind the registration, `X-Real-IP` / `CF-Connecting-IP` are no longer read. This is
  accepted and documented, with Cloudflare ranges and `ForwardLimit = 2`, or NPM's real-IP rewrite.
- **Q4, strictness:** the new registration is strict; transport configs stay lenient (log and skip). Making those strict
  is a follow-up task → deferred to TASK-830.
- **Q5, a differing WebSocket list:** `WebSocketAuthenticationService` takes the optional `TrustedProxySettings` from DI.
  - When its own `TrustedProxies` is non-empty and differs from the shared list, it throws `InvalidOperationException`
    naming both lists. When its own list is empty, it uses the shared one.
  - A startup filter on the WebSocket registration resolves the service eagerly, so the refusal fires at host start.
- **Q6, looked up:** the WebSocket config comes from `IOptions<WebSocketAuthenticationConfiguration>`, which
  Security.AspNetCore can't see. So Q5's check lives in Birko.Communication.WebSocket.
- **Q7, configuration shape:** one section, `Birko:ForwardedHeaders`, with `TrustedProxies`, `ForwardLimit`,
  `AllowedHosts` and `ForwardProto`, through `AddBirkoForwardedHeaders(IConfigurationSection)` and an `Action<>`
  overload. The list binds into `TrustedProxySettings`, the rest into `BirkoForwardedHeadersOptions`.
- **Step 6 (loopback) reads `TrustedProxySettings`** from DI. Birko.Security is already a dependency.
- **Q10, human test:** run locally against Birko.Sandbox behind an nginx container. The godzilla check moves to the
  Symbio follow-up note.

### Findings that shape the design
- **Only one place reads the client IP from an `HttpContext`:** `Birko.Communication.WebSocket/Services/WebSocketAuthenticationService.cs:62`.
  It passes `Connection.RemoteIpAddress` and the headers to `AuthenticationService.ResolveClientIp`. REST, SOAP and SSE
  run on `HttpListener`, which ASP.NET's forwarded headers never touch.
- **After ASP.NET's middleware, Birko walks `X-Forwarded-For` again.** The two disagree in two cases:
  - `ForwardLimit` stops while the peer is still trusted (Cloudflare→NPM, both trusted). Birko's walk has no limit.
  - Every hop is consumed and the leftmost one is inside a trusted CIDR. Birko then falls back to `X-Real-IP` /
    `CF-Connecting-IP`, which ASP.NET never reads and a client can send itself.
  - So the fix is one decision point, not two walks kept in step.
- **`AuthenticationService.IsTrusted` silently skips a malformed entry.** An unset `${VAR}` stays literal, and a blank
  entry is dropped (rule 60).
- **WebApplication adds `UseAuthentication` ahead of the app's pipeline when the app doesn't call it.** A user-placed
  `Use…` cannot be guaranteed to run first. A front-inserting `IStartupFilter` can, as ASP.NET's own
  `ForwardedHeadersStartupFilter` does.

### Steps
1. **`Birko.Security/Authentication/TrustedProxyList.cs`** (projitems):
   - `Parse` expands each `${VAR}` and splits the list into addresses and `IPNetwork`s.
   - It throws `FormatException` naming the entry for: a non-address, a bad CIDR, a literal `${VAR}` left after
     expansion, or a blank entry.
   - It also has `Contains(IPAddress)` (IPv4-mapped too), `IsEmpty` and `ResolvedItemKey`.
   - `AuthenticationService` reuses it (one producer, rule 16), but stays lenient (log and skip), so REST, SOAP and SSE
     behave as before.
2. **`Birko.Security.AspNetCore/Authentication/BirkoForwardedHeadersOptions.cs`:** `ForwardLimit = 1`,
   `AllowedHosts = []`, `ForwardProto = true`, and **no proxy fields**. They live here, not on `AuthenticationConfiguration`,
   because the `HttpListener` transports would accept and ignore them (rule 55).
3. **`Extensions/ForwardedHeadersExtensions.cs`:** `AddBirkoForwardedHeaders<TConfig>(configure?)` and a factory overload.
   - It registers a singleton `ForwardedHeadersState`.
   - `Configure<ForwardedHeadersOptions>` clears `KnownProxies` / `KnownIPNetworks` and adds the parsed entries.
   - It sets `XForwardedFor`, `XForwardedProto` when `ForwardProto` is set, `XForwardedHost` only when hosts are listed,
     and `ForwardLimit`. This also tames `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.
4. **`Extensions/ForwardedHeadersStartupCheck.cs` (`IStartupFilter`):**
   - It parses strictly. A failure is an `InvalidOperationException` wrapping the `FormatException` and naming the config
     type (rule 9).
   - It refuses `AllowedHosts` with no proxies, and `ForwardLimit < 1` (rule 55).
   - It inserts at the **front** a middleware that runs `ForwardedHeadersMiddleware` and sets
     `context.Items[ResolvedItemKey]`. With an empty list it adds nothing.
   - It sets the `app.Properties["Birko.Security.ForwardedHeaders"]` marker.
   - Ordering before authentication, the rate limiter, the loopback bypass and the tenant resolvers then holds by
     construction.
5. **One decision point:** `WebSocketAuthenticationService.GetClientIpAddress` returns `Connection.RemoteIpAddress` when
   `Items` carries the key. A client cannot set `Items`. Without the registration, nothing changes.
6. **Loopback refuses to start:** `LoopbackBypassStartupCheck` resolves `ForwardedHeadersState` and throws when the bypass
   is on and the list is non-empty. This is read at `Configure` time, so the order of the `Add*` calls doesn't matter.
7. **Ordering docs:** XML docs on `SubdomainTenantResolver`, `TenantMiddleware`, `UseBirkoLoopbackBypass` and
   `BirkoSecurityOptions.LoopbackBypass`. A manual extra `UseForwardedHeaders()` is harmless, because the peer is
   untrusted by then; document it.
8. **Docs:**
   - `docs/security.md` gets a "Behind a reverse proxy" section: pipeline order, the godzilla Cloudflare→NPM example and
     the `CF-Connecting-IP` caveat. Its loopback warning is rewritten as enforced.
   - Update the READMEs and CLAUDE.md files of Birko.Security, Birko.Security.AspNetCore and
     Birko.Communication.WebSocket, and add a Recent Updates entry.
9. **Consumer follow-ups** go into this file as a section; no consumer file is edited.
   - **Presenter:** its two arrays become one `TrustedProxies` list (its config keys change).
   - **Symbio:** `UseSymbioForwardedHeaders` and the per-stack `KnownProxies__0` compose variables.
   - **Affiliate:** verify the trust-everyone finding first.

### Tests
- **`tests/Birko.Security.Tests/Task829TrustedProxyListTests.cs`:**
  - address, CIDR and IPv4-mapped matching
  - throws on `10.0.0.300`, `10.0.0.0/40`, `unknown`, a unique unset `${BIRKO_T829_UNSET_<guid>}` (rule 61) and a blank
    entry
  - the existing Task165/Task651 tests stay green
- **`tests/Birko.Security.AspNetCore.Tests/Extensions/Task829ForwardedHeadersTests.cs`** on real Kestrel at `127.0.0.1:0`:
  - (a) **Agreement:** `RemoteIpAddress`, `Scheme` and `WebSocketAuthenticationService.GetClientIpAddress` all give
    `203.0.113.7` / `https`.
  - (b) **Divergence regression:** a trusted CIDR containing the client, plus `X-Real-IP`. Red without step 5.
  - (c) **Nothing configured:** a spoofed `X-Forwarded-For` is ignored, even with a manual `UseForwardedHeaders()`.
  - (d) **Host forwarding:** `X-Forwarded-Host` ignored, honoured, or refused, per `AllowedHosts`.
  - (e) **Startup failures:** malformed entry, hosts without proxies, `ForwardLimit = 0`.
  - (f) **Ordering:** forwarded headers run before authentication and the subdomain resolver.
  - (g) **Header counts:** `X-Forwarded-For` / `-Proto` count mismatch pinned.
  - The test project imports the `Birko.Communication.WebSocket` projitems and its dependencies.
- **`LoopbackBypassTests`:** bypass on plus trusted proxies refuses to start in either `Add*` order; bypass alone still
  starts.
- **`tests/Birko.Communication.WebSocket.Tests`:** the `Items` key short-circuits the walk; without it the walk is
  unchanged.

### Tradeoffs and risks
- **Front-inserting filter vs explicit `Use`:** the filter guarantees order, including ahead of an auto-added
  `UseAuthentication`, but is less visible in `Program.cs`.
- **`Items` short-circuit vs mirroring ASP.NET's walk:** the short-circuit means one resolver per host type. Mirroring
  would leave two algorithms that drift (rule 53).
- **Fallback headers are lost** behind the registration. Cloudflare deployments must trust Cloudflare's ranges.
  `HostFilteringStartupFilter` still sees the pre-forwarding `Host`.
- **The loopback check sees only a list registered through `AddBirkoForwardedHeaders`.** Document this.
- **`KnownNetworks` is obsolete on net10;** use `KnownIPNetworks`.
- **Corrected at close (2026-11-07, review):** step 7's "a manual extra `UseForwardedHeaders()` is harmless" was
  wrong. A second pass believed a client-written hop whenever the first stopped on a trusted address (measured). The
  registration now removes the `X-Forwarded-*` values it left unread (rule 67). The strict parser also refuses
  `0.0.0.0/0` / `::/0`, short (`10.5`) and bracketed (`[::1]:80`) addresses, and `/32` / `/128` equals the bare
  address.
- **Steps 1, 8 and 9 were flagged as separable.** They stay here, because each one implements one of this task's own
  criteria.
