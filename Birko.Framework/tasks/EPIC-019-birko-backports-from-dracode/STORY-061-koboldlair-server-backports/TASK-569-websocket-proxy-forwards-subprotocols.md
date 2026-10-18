---
id: TASK-569
parent: STORY-061
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-12
depends-on: [TASK-542]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# MapWebSocketProxy drops the client's requested sub-protocols, so a client that asks for one fails its handshake

## Context

Spawned from TASK-542's close review. `MapWebSocketProxy` connects upstream with no `Sec-WebSocket-Protocol` and
accepts the client with none. A `ClientWebSocket` that requested a sub-protocol (`graphql-transport-ws`, `mqtt`, …)
refuses an accept that selects none, so such a client cannot use the proxy at all. DraCode's `/dragon` and `/wyvern`
request none, which is why TASK-542's acceptance did not cover it.

## Acceptance criteria

- [x] The client's requested sub-protocols are offered upstream, in the client's order
- [x] The client is accepted with the sub-protocol the upstream selected (none when it selected none)
- [x] Tests: a client requesting a protocol the upstream supports gets it end to end; one the upstream rejects is
      answered as the upstream answered (502 when the handshake fails)

## Out of scope

- Forwarding other handshake headers (cookies, origin): the credential options cover authentication

## Human test plan

N/A — the handshake is asserted end to end against a real Kestrel upstream, as in TASK-542's suite.

## Outcome (2026-10-18)

- **Fix.** `MapWebSocketProxy` offers the client's requested sub-protocols upstream, in the client's order, and accepts
  the client with whatever the upstream selected (none when it selected none).
- **Measured on HEAD.** A client asking for `mqtt` and `graphql-transport-ws` was accepted with no sub-protocol, and the
  upstream was offered nothing, so such a client could not use the proxy.
- **Tests.** `WebSocketProxySubProtocolTests`, 4, on a real Kestrel upstream and proxy.
  - Red on HEAD: 2 (negotiated end to end; an upstream that selects none passes that on).
  - Pins: 2 (a client that requests none gets none; an upstream refusing the handshake is answered 502).
- **Regression.** Birko.Communication.WebSocket.Tests 92/92, the only importer. Sandbox builds.
- **Docs.** The project CLAUDE.md and README, and `communication.md`.
