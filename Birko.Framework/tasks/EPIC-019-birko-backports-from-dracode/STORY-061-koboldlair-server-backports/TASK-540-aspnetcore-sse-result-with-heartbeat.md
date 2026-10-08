---
id: TASK-540
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

# Birko.Communication.SSE: an ASP.NET Core minimal-API result with heartbeat and terminal stop

## Context

DraCode's `Api/RunsEndpoints.cs` L134–194 (`StreamRunEventsAsync`, `WriteFrameAsync`): subscribe first, then send
`text/event-stream` headers (`no-cache`, `X-Accel-Buffering: no`) and flush; read a `ChannelReader`, write
`id:` / `event:` / `data:` frames; send a `: heartbeat` comment when idle; stop on a terminal predicate. Symbio hand-rolls
`/api/sse` too (`SseConnectionManager.cs`, 98). `Birko.Communication.SSE` is built around its own HttpListener-style
`SseServer` / `ISseMiddleware`; `SseEvent.ToString()` already formats frames, but there is no `IResult` / `HttpResponse`
helper.

P3 because .NET 10's `TypedResults.ServerSentEvents` covers the core; what it lacks is the heartbeat and the terminal
stop.

Adopted in the consumer by DraCode TASK-138 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] An `IResult` (or `HttpResponse` extension) over a `ChannelReader` / `IAsyncEnumerable` of `SseEvent`
- [ ] Heartbeat interval, terminal predicate, proxy-friendly headers, client disconnect ends the loop
- [ ] Reuses `SseEvent` formatting — one producer for the frame text
- [ ] Feeds from [[TASK-533]]'s broadcaster without glue
- [ ] Tests with `TestServer`: frames, heartbeat when idle, terminal stop, disconnect

## Human test plan

N/A — covered by tests.

## Implementation plan
