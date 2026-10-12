---
id: TASK-542
parent: STORY-061
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A WebSocket reverse-proxy endpoint mapper

## Context

`DraCode.KoboldLair.Client/Program.cs` (190) proxies `/dragon` and `/wyvern` to the server with two duplicated handlers
and a `RelayWebSocketAsync`. Birko has nothing for this; [[TASK-526]] covers only the client's `/api/config`. DraCode's
copy has security problems the generic version must not have (DraCode fixes its own in its TASK-119):

- TLS certificate validation is unconditionally disabled
- the token is concatenated into the query string without URL-encoding
- the close status is not forwarded

Adopted in the consumer by DraCode TASK-140 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] `MapWebSocketProxy(pattern, upstream, …)` in `Birko.Communication.AspNetCore` or `.WebSocket`
- [x] Certificate validation on by default; any opt-out is explicit and named as unsafe
- [x] Upstream credentials attached as a header or properly encoded query value, configurable
- [x] Bidirectional relay forwards message type, `EndOfMessage` and close status / description; either side closing closes the other
- [x] Tests: relay both ways, fragmented message, close propagation, upstream refusal

## Out of scope

- Sub-protocol negotiation: a client that requests one fails its handshake through the proxy → TASK-569
- Forwarding the upstream's own refusal status (401/403) to the client: every upstream failure answers 502, since the
  client authenticated to the proxy, not to the upstream

## Human test plan

N/A — covered by tests.

## Implementation plan

Home: `Birko.Communication.WebSocket/Proxy/` — that project already owns ASP.NET Core WebSocket endpoint mapping
(`MapWebSocketEndpoint`); `Birko.Communication.AspNetCore` is OwnedCrud only and would gain a WebSocket dependency.

1. **`WebSocketProxyOptions`** — `Upstream` (`ws`/`wss`; `http`/`https` mapped to them), `Credential`
   (`Func<HttpContext, string?>`, default null = forward nothing), `CredentialTransport` (`Header`, default — a query
   string is logged by proxies; `QueryString` for upstreams that read `?token=`, e.g. Birko's own gate),
   `CredentialHeaderName` (`Authorization`) + `CredentialHeaderScheme` (`Bearer`), `CredentialQueryParameter`
   (`token`), `DangerousAcceptAnyServerCertificate` (false), `ConfigureClient` (`Action<ClientWebSocketOptions>`),
   `CloseTimeout` (5 s), `BufferSize` (16 KiB).
2. **Upstream URI** — the query value goes through `Uri.EscapeDataString`, appended to any query the upstream
   already carries. One producer (`WebSocketProxyOptions.BuildUpstreamUri`), unit-tested.
3. **`WebSocketRelay`** — frame-by-frame pump each way, so nothing is reassembled and memory stays bounded at one
   buffer per direction (no second reassembly loop, CLAUDE.md § Messaging). Each frame is sent with its own
   `MessageType` and `EndOfMessage`. A received Close → `CloseOutputAsync` on the other side with the same status and
   description (no status → `Empty`), then wait up to `CloseTimeout` for the other pump to see the echo; past it,
   abort. A side that drops without a Close → the other gets `EndpointUnavailable`. Either way both sockets end.
4. **`MapWebSocketProxy(pattern, upstream, configure?)`** on `IEndpointRouteBuilder`, returning the convention
   builder (so `.RequireAuthorization()` chains). Non-upgrade request → 400. **Connect upstream first, accept the
   client second**: an upstream refusal or unreachable host answers the client **502** without upgrading, instead of
   DraCode's accept-then-fail.
5. **Tests** (real Kestrel on port 0 for the upstream and the proxy, `ClientWebSocket` as the client): text and binary
   relay both ways; a fragmented message arrives as the same frames with `EndOfMessage` preserved; client close
   status/description reaches the upstream and upstream close reaches the client; upstream refusal → 502, no upgrade;
   credential as header and as encoded query (a value with `&`, `=`, space); certificate validation is not disabled
   by default (the callback stays null unless the unsafe flag is set).
6. **Docs** — project CLAUDE.md + README, framework `Recent Updates` entry.

## Progress log

- 2026-10-12 — `Birko.Communication.WebSocket/Proxy/`: `MapWebSocketProxy`, `WebSocketProxyOptions`, internal
  `WebSocketRelay`. 15 new tests in `WebSocketProxyTests` against real Kestrel upstream + proxy; suite 71/71, three runs.
  Mutation-checked, each red: close status forced to NormalClosure (2 tests), `EndOfMessage` forced true (fragmented),
  query value unencoded (credential-as-query), certificate callback always set (default-validation), no close on a drop
  (EndpointUnavailable), accept-before-connect as DraCode did (both 502 tests).
