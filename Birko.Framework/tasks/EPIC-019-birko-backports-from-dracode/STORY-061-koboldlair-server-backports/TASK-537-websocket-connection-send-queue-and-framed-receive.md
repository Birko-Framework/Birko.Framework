---
id: TASK-537
parent: STORY-061
feature: null
status: done
picked-by: fix-next
priority: P1
assignee: ai
created: 2026-10-08
depends-on: []
blocks: [TASK-539]
pr: null
github-issue: null
jira-key: null
---

# Birko.Communication.WebSocket: one capped whole-message receive, used by WebSocketServer and offered to handlers

> Filed as "a connection with a serialized send queue and a framed receive loop". **Rescoped 2026-10-08, before any
> code, on measurement** (fix-next step 3; rule 54). The send half was aimed at a defect that does not reproduce; the
> receive half held, and turned up a real Birko defect. The original text is in git history.

## Context

**The send premise is falsified on the runtime Birko targets.** The filing assumed concurrent `SendAsync` on one socket
corrupts or throws, as the `System.Net.WebSockets.WebSocket` docs say ("exactly one send and one receive is supported …
in parallel"). Measured on .NET 10.0.12, `WebSocket.CreateFromStream` (the `ManagedWebSocket` that both Kestrel and
`HttpListener` hand out): **200 concurrent `SendAsync` of 100 KB each, three runs — 0 exceptions, 200/200 received,
0 corrupt.** `ManagedWebSocket` queues concurrent sends behind its own send mutex. So:

- `WebSocketServer.BroadcastAsync` sending to a client while another send is in flight is **not** a defect today.
  Adding a gate would add a guard whose test cannot fail (rule 53). Instead a test **pins the runtime behaviour** the
  framework relies on, so a runtime that stops serializing turns it red.
- A send-queue connection type (DraCode's `WebSocketSender`, Symbio's per-socket gate) solves nothing on this runtime.
  Not built. DraCode's adoption (DraCode TASK-135) can delete `WebSocketSender` and send directly.

**The receive half holds, and has a Birko defect in it.** A message may span frames, and the ASP.NET Core mapping hands
handlers a raw socket, so each consumer writes its own loop — DraCode's `DragonService` and `WyrmService` ignore
`EndOfMessage` with a 64 KB buffer (a larger message parses as partial JSON) and `KoboldEndpointService` reads its first
message uncapped. Birko's own `Servers/WebSocketServer.cs` `ReceiveLoopAsync` does reassemble across `EndOfMessage` —
**into a `MemoryStream` with no size limit**. Any client can send frames that never set `EndOfMessage` and grow the
server's memory without bound. Reachable from untrusted input, silent until the process dies.

Adopted in the consumer by DraCode TASK-135 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] One whole-message receive in `Birko.Communication.WebSocket` (an extension on `System.Net.WebSockets.WebSocket`): reassembles across `EndOfMessage`, enforces a size cap, reports a close as a result rather than an exception
- [x] Over the cap it closes the socket with `MessageTooBig` and says so to the caller — never returns a truncated message
- [x] `WebSocketServer` receives through it (one producer, rule 16), with a configurable cap and a documented default
- [x] Regression test: a client streaming frames past the cap without `EndOfMessage` gets closed with `MessageTooBig`; proven red on the current loop
- [x] Tests: fragmented message reassembled, text and binary, close surfaced, exact-cap boundary
- [x] Concurrent `SendAsync` on one socket pinned by a test as runtime behaviour (no gate), with the measurement above in its doc
- [x] DraCode TASK-135 and Birko TASK-539 updated to the rescoped API

## Out of scope

- A send-queue / connection type — falsified above; reopen only on a runtime that measurably fails the pinned test

## Human test plan

N/A — covered by tests.

## Implementation plan

## Progress log

- step 2 — picked at the user's request after TASK-536; next in STORY-061 and P1
- step 3 — verified: rescoped. Concurrent-send premise falsified (scratch probe, .NET 10.0.12, 3×200 concurrent 100 KB sends: 0 threw, 0 corrupt). Receive half held; new finding: `WebSocketServer.ReceiveLoopAsync` buffers a message with no cap (unbounded memory from untrusted input). Context and criteria rewritten before code
- step 4 — layer: local
- step 5 — fix in `Birko.Communication.WebSocket/Messaging/WebSocketMessageExtensions.cs` (new) and `Servers/WebSocketServer.cs` (receive loop + `MaxMessageBytes`); tests in `tests/Birko.Communication.WebSocket.Tests/WebSocketMessageReceiveTests.cs`; suite 52/52 green, no nullable warnings
- step 6 — reverted the server's receive loop to HEAD (kept a plain `MaxMessageBytes` property so the suite compiles): 2/52 failed; fix-dependent = Server_ClientStreamingPastCapWithoutEndOfMessage_IsClosedWithMessageTooBig (timed out: the old loop kept buffering), Server_NonPositiveMaxMessageBytes_Throws; the 7 extension tests cover new API and cannot be reverted onto old code — they pin its contract; ConcurrentSendsOnOneSocket_AllArriveIntact is a runtime contract pin, not evidence
- step 7 — no spec area covers Birko.Communication.WebSocket (TASK-226); docs: project README (fixed a wrong standalone-server sample; new "Receiving whole messages"), project and test CLAUDE.md (removed a `WebSocketConnection` entry for a class that never existed), CHANGELOG, Recent Updates (9.96 KB — at the budget; the next entry rolls). DraCode TASK-135 and Birko TASK-539 rewritten to the rescoped API
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log

## Outcome

**What was fixed.** Birko's standalone `WebSocketServer` buffered an incoming message across frames with no size limit,
so any client could grow server memory without bound by never finishing a message. It now receives through a new
`ReceiveMessageAsync` extension that reassembles a whole message, caps it (`MaxMessageBytes`, default 4 MiB) and closes an
over-cap client with `MessageTooBig`. The same extension is what ASP.NET Core handlers should call instead of a single
`ReceiveAsync` that returns a fragment.

**Proof.** Reverting the server loop fails 2 of 52: the end-to-end test where a real client streams past the cap
(old code: no close, test times out) and the cap validation. Everything else passes either way — the extension tests
describe new API, and the concurrent-send test pins runtime behaviour.

**Judgement calls.**
- *Rescoped before code, on measurement, instead of building the filed send queue.* The queue answered a documented
  restriction that the runtime does not enforce: 3 × 200 concurrent 100 KB sends, 0 errors, 0 corruption. Building it
  anyway would ship a guard whose regression test cannot fail (rule 53). The stricter option — a gate "because the docs
  say so" — was rejected for that reason; the pin test turns red if the runtime changes, and its doc names the gate as
  the fix then.
- *Outcome enum, not exceptions, for close and over-cap.* Both are normal ends of a connection; an exception would push
  every handler back into try/catch around the receive.
- *4 MiB default.* Large enough for DraCode's plan/chat payloads, small enough to bound a hostile client. It is a
  behaviour change for a server that received bigger messages before — CHANGELOG says to raise the property.
- *Close echo uses `CloseOutputAsync`,* not `CloseAsync`, so it never waits on a receive the caller may be doing.

**Flagged, not fixed.** Nothing new. DraCode's `WebSocketSender` (reports failed sends as delivered) is deleted by
DraCode TASK-135.
