---
id: TASK-539
parent: STORY-061
feature: null
status: todo
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

- [ ] Handler registration by command name; typed `data` deserialization; unknown command → error reply
- [ ] Every reply, including errors and deserialization failures, carries the request id when one was sent
- [ ] Wire shape matches what TASK-523's `WsClient.request` expects — one contract, tested from both sides if practical
- [ ] Tests: success, handler throws, unknown command, malformed JSON, missing id

## Human test plan

N/A — covered by tests.

## Implementation plan
