---
id: TASK-523
parent: STORY-059
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# WsClient: request/response with id correlation and timeout

## Context

DraCode's `DraCode.KoboldLair.Client/src/services/api-client.ts` (234 lines) keeps a pending-request map over `WsClient`:
it stamps each message with an id, resolves the promise when the reply with that id arrives, and rejects after 30 s. About
90 lines of that are generic — any app using `WsClient` for commands needs the same. (It also re-derives "connected" as
`ws !== null` because it missed `WsClient.connected` / `state`.)

Consumer side: DraCode `EPIC-018 / STORY-039` (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] `WsClient.request(type, data, { timeoutMs })` (or a small companion) returns a promise resolved by the reply carrying the same correlation id; rejects on timeout and on disconnect
- [x] The correlation field name is configurable (DraCode uses `id` / `requestId`)
- [x] Unsolicited messages still reach the normal handlers
- [x] Tests: reply resolves, timeout rejects, disconnect rejects pending requests

## Out of scope

- App-specific command methods (stay in apps)

## Human test plan

N/A — covered by unit tests.

## Implementation plan

Drafted at pick, 2026-10-14. DraCode's server replies `{ id, type: 'response', data }` / `{ id, type: 'error',
error }` (`WebSocketCommandHandler.cs`), id at the envelope's top level; its client reads the id from inside `data`,
so it never matches. So correlation is on the envelope.

1. `WsClientOptions.requestIdField` (default `'id'`). `request<T>(type, data, { timeoutMs = 30000, errorTypes =
   ['error'] })` sends `{ type, [idField]: id, data }`; `requestMessage<T>(body, opts)` sends any object with the id
   added (DraCode's `{ id, command, data }`). A reply with that id resolves with its `data`, or rejects when its `type`
   is an error type (message from `error` / `message`). Timeout rejects; a socket close or `disconnect()` rejects every
   pending request; calling while not open rejects at once. A correlated reply is consumed; everything else is
   dispatched as before.
2. Playground smoke with a fake socket (the global `WebSocket` swapped only for the synchronous `connect()` call).

## Progress log

- 2026-10-14 — Done, released in **Birko.Web v0.1.8** (Web `ffc40f8`, Playground `bd1de6d`). Correlation is on the
  envelope's top level both ways, matching DraCode's `WebSocketCommandHandler`; `requestMessage()` covers its
  `{ id, command, data }` shape. A correlated reply is consumed rather than also dispatched. `ws-request-smoke` 16/16;
  with correlation, the disconnect rejection and the timeout broken the suite never settles and `verify.mjs` fails it
  as never reported. DraCode TASK-116 unblocked (DraCode `c5cef08`), with a note that its `api-client.ts` reads the id
  from inside `data`, so its replies may never match today.
