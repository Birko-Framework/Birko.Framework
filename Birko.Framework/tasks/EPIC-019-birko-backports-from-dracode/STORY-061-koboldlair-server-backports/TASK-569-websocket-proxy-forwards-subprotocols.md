---
id: TASK-569
parent: STORY-061
feature: null
status: todo
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

- [ ] The client's requested sub-protocols are offered upstream, in the client's order
- [ ] The client is accepted with the sub-protocol the upstream selected (none when it selected none)
- [ ] Tests: a client requesting a protocol the upstream supports gets it end to end; one the upstream rejects is
      answered as the upstream answered (502 when the handshake fails)

## Out of scope

- Forwarding other handshake headers (cookies, origin): the credential options cover authentication

## Human test plan

N/A — the handshake is asserted end to end against a real Kestrel upstream, as in TASK-542's suite.
