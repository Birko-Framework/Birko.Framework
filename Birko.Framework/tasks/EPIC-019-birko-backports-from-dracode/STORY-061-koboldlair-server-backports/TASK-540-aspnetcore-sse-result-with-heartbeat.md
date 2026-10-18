---
id: TASK-540
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

- [x] An `IResult` (or `HttpResponse` extension) over a `ChannelReader` / `IAsyncEnumerable` of `SseEvent`
- [x] Heartbeat interval, terminal predicate, proxy-friendly headers, client disconnect ends the loop
- [x] Reuses `SseEvent` formatting — one producer for the frame text
- [x] Feeds from [[TASK-533]]'s broadcaster without glue
- [x] Tests with `TestServer`: frames, heartbeat when idle, terminal stop, disconnect

## Human test plan

N/A — covered by tests.

## Outcome (2026-10-18)

New project **`Birko.Communication.SSE.AspNetCore`**. It is separate from `Birko.Communication.SSE` so that SSE clients
need no ASP.NET Core. A FrameworkReference in a projitems would be NETSDK1087 for importers that already have one
(DraCode's Web SDK, Symbio through `Birko.Data.Tenant`), so, as with `Birko.Communication.AspNetCore`, the host supplies
`Microsoft.AspNetCore.App`.

- **`SseResults.Stream(...)`.** It takes a `ChannelReader<T>` or `IAsyncEnumerable<T>` with a mapper, or
  `IAsyncEnumerable<SseEvent>` as is.
  - Headers: `text/event-stream`, `Cache-Control: no-cache` and `X-Accel-Buffering: no`, with buffering off and the
    headers flushed before the first item.
  - Frames: every frame, the heartbeat included, is `SseEvent.ToString()`, so the frame text has one producer.
  - Heartbeat: a `: heartbeat` comment when idle (15 s; `InfiniteTimeSpan` turns it off, a non-positive interval is
    refused), and an optional `retry:` frame at the start.
  - End: the stream ends when the source completes, after an item matching `isTerminal` (that item is sent first), or on
    client disconnect. `owns` is disposed in every case.
- **[[TASK-533]]'s `KeyedEventBroadcaster`** plugs in directly: subscribe in the handler before returning, then pass
  `subscription.Reader`, a mapper and `owns: subscription`.
- **Safety.** The in-flight `MoveNextAsync` is kept across heartbeats, since an enumerator must not be advanced twice at
  once. On a disconnect the enumerator is disposed only after that pending move settles, because an async iterator
  throws when disposed under one.
- **Tests.** `SseResultsTests`, 8 over TestServer and stable over 4 runs: frames, headers and the source ending;
  terminal stop; idle heartbeats with a later item still delivered; `retry:`; disconnect releasing the source and
  `owns`; the broadcaster feed; a refused heartbeat. The API is new; mutations instead: removing the terminal check fails
  3, and removing the heartbeat branch fails 1.
- **Regression.** SSE.Tests 25, SSE.AspNetCore.Tests 8. Sandbox builds (import committed there).
- **Docs.** `communication.md` § Birko.Communication.SSE.AspNetCore, the project CLAUDE.md and README, and the catalog,
  Dependency Flow and dependencies. Communication is out of the spec map.
