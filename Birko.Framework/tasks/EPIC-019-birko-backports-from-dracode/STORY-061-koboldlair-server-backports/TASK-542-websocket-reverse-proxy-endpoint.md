---
id: TASK-542
parent: STORY-061
feature: null
status: todo
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

- [ ] `MapWebSocketProxy(pattern, upstream, …)` in `Birko.Communication.AspNetCore` or `.WebSocket`
- [ ] Certificate validation on by default; any opt-out is explicit and named as unsafe
- [ ] Upstream credentials attached as a header or properly encoded query value, configurable
- [ ] Bidirectional relay forwards message type, `EndOfMessage` and close status / description; either side closing closes the other
- [ ] Tests: relay both ways, fragmented message, close propagation, upstream refusal

## Human test plan

N/A — covered by tests.

## Implementation plan
