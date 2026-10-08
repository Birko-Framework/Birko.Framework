---
id: TASK-537
parent: STORY-061
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-10-08
depends-on: []
blocks: [TASK-539]
pr: null
github-issue: null
jira-key: null
---

# Birko.Communication.WebSocket: a connection with a serialized send queue and a framed receive loop

## Context

A WebSocket permits one outstanding `SendAsync`, and a message can span several frames. Birko's ASP.NET Core
middleware hands consumers a raw `System.Net.WebSockets.WebSocket` and leaves both to them. Two consumers wrote the gate
independently:

- DraCode: `Services/WebSocketSender.cs` (181), plus hand-written receive loops in `DragonService.cs` (~L534),
  `WyrmService.cs` (L54–84) and `KoboldEndpointService.cs` (185)
- Symbio: `Symbio.Infrastructure/Realtime/WebSocketConnectionManager.cs` (115), whose comment reads "A WebSocket permits
  only ONE outstanding SendAsync"

What the gap produced in DraCode: `WyrmService` sends from a `Task.Run` while its receive loop also sends (concurrent
`SendAsync`); `DragonService` and `WyrmService` ignore `EndOfMessage` with a 64 KB buffer, so a large message is parsed as
partial JSON; `KoboldEndpointService` reads its first message with no size cap.

⚠ **Do not copy `WebSocketSender` as is (rule 22).** It calls `TrySetResult` on `WebSocketException`, on drain and on a
closed socket — it reports undelivered messages as sent — and its queue is unbounded.

**Birko defect on the same theme:** `Servers/WebSocketServer.cs` `BroadcastAsync` sends to each client with no
per-client gate, so two overlapping broadcasts (or a broadcast and a direct send) write to one socket at once.

Adopted in the consumer by DraCode TASK-135 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] A connection type wrapping the socket: one send at a time, a timeout per send, a bounded queue with a declared full-queue policy
- [ ] An undelivered message faults its send task or returns false — never success
- [ ] Receive reassembles a whole message across `EndOfMessage` with a size cap; close is detected and surfaced
- [ ] Send-failure and disconnect events; queue depth observable
- [ ] `WebSocketServer.BroadcastAsync` goes through the same per-client gate; regression test with overlapping broadcasts, proven red first
- [ ] Tests: concurrent senders, fragmented message, oversize message, send after close

## Human test plan

N/A — covered by tests.

## Implementation plan
