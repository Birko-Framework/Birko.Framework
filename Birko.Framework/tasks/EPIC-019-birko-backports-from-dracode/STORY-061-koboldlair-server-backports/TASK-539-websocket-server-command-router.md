---
id: TASK-539
parent: STORY-061
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-08
depends-on: [TASK-537]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Communication.WebSocket: a server-side command router (the server half of TASK-523)

## Context

[[TASK-523]] gives `WsClient` request / response with id correlation. Nothing on the server side answers it. DraCode's
`Services/WebSocketCommandHandler.cs` (161) + `Models/WebSocket/WebSocketCommand.cs` (11) route `{id, command, data}` to
a handler by command name and reply `{id, type: "response", data, timestamp}` or `{id, type: "error", error}`; an unknown
command is an error.

Defect to fix in the generic version: `SendErrorAsync(webSocket, null, …)` drops the request id, so a correlating client
waits out its 30 s timeout instead of failing at once. Requests are read with [[TASK-537]]'s capped
`ReceiveMessageAsync`; replies may use `SendAsync` directly — TASK-537 measured that concurrent sends on one socket are
serialized by the runtime, so no send queue is needed.

Adopted in the consumer by DraCode TASK-137 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`). The handler classes themselves stay in DraCode.

## Acceptance criteria

- [x] Handler registration by command name; typed `data` deserialization; unknown command → error reply
- [x] Every reply, including errors and deserialization failures, carries the request id when one was sent
- [x] Wire shape matches what TASK-523's `WsClient.request` expects — one contract, tested from both sides if practical
- [x] Tests: success, handler throws, unknown command, malformed JSON, missing id

## Human test plan

N/A — covered by tests.

## Outcome (2026-10-18)

New `Birko.Communication.WebSocket.Messaging.WebSocketCommandRouter`, with these entry points:
- `Map(name, handler)` and `Map<TData>(name, handler)`, where `data` is deserialized camelCase and case-insensitive.
- `HandleAsync(text)`, which returns the reply JSON and never throws for a bad request.
- `HandleAsync(socket, text)`.
- `RunAsync(socket)`, which reads with TASK-537's capped `ReceiveMessageAsync` and handles requests concurrently.

**Replies.** `{ id, type: "response", data, timestamp }` or `{ id, type: "error", error, timestamp }`.

**The contract is WsClient's, and DraCode's differed.**
- `WsClient.request(type, data)` sends the command name in `type` (`{ type, id, data }`), while DraCode's handler read
  `command`. `CommandField` therefore defaults to `type`, and DraCode can set `command` until its client moves.
- `IdField` mirrors WsClient's `requestIdField`.
- Replies are checked for what WsClient's `_settleReply` reads: a string id, `type` `"error"` with a string `error`,
  otherwise `data`.

**DraCode's defect, fixed.**
- Every reply carries the request id when one was sent (DraCode sent `null` on every error, so the client timed out):
  unknown command, missing command, unreadable typed `data`, a failing handler, a result that will not serialize.
- A request that is not JSON or not an object has no id to echo, and gets an error without one.
- A numeric id is echoed as sent.

**Changed from DraCode (a library default).**
- An unexpected exception's message is logged, not sent: the client gets `Command 'x' failed.`, because a message can
  carry paths or SQL. `ExposeExceptionMessages` restores DraCode's behaviour.
- `WebSocketCommandException` fails a command with a client-facing message.
- A command maps once.
- A binary message gets an error reply.

**Tests.** `WebSocketCommandRouterTests`, 17 cases: success, typed data, case-insensitive names, five error kinds each
with the id, the hidden vs exposed exception message, malformed JSON / non-object / empty, a missing id, a numeric id,
configurable fields, and a duplicate map. Over a real socket pair, a slow command does not hold up the next, a binary
message gets an error, and the run ends on close. The API is new, so it cannot be red on HEAD; mutation instead: error
replies built without the id fail 5.

**Regression.** Birko.Communication.WebSocket.Tests 88/88 (the only importer); Sandbox builds.

**Docs.** The project's CLAUDE.md and README, and docs/communication.md. Communication is out of the spec map's scope.
