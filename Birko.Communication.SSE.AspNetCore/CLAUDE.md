# Birko.Communication.SSE.AspNetCore

## Overview
Server-Sent Events from an ASP.NET Core minimal-API endpoint: `SseResults.Stream(...)` returns an `IResult` that streams a
`ChannelReader<T>` or `IAsyncEnumerable<T>` as SSE frames, with a heartbeat when idle and a terminal stop (TASK-540).
.NET 10's `TypedResults.ServerSentEvents` covers the core but has neither.

## Project Location
`C:\Source\Birko\Framework\Birko.Communication.SSE.AspNetCore\`

## Components
- **`SseResults.Stream<T>(ChannelReader<T> | IAsyncEnumerable<T>, toEvent, isTerminal, options, owns)`** and
  `Stream(IAsyncEnumerable<SseEvent>, ...)`:
  - headers `text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no` (nginx), response buffering disabled,
    headers flushed before the first item;
  - each item written as `SseEvent.ToString()` — **the one frame formatter** (rule 16), heartbeat included
    (`SseEvent.CreateComment`);
  - ends when the source completes, after an item matching `isTerminal` (sent first), or when the client disconnects
    (`RequestAborted`); `owns` is disposed in every case.
  - The pending `MoveNextAsync` is kept across heartbeats (an enumerator may not be advanced twice at once), and on a
    disconnect the enumerator is disposed only after it settles (an async iterator throws when disposed under one).
- **`SseStreamOptions`**: `HeartbeatInterval` (15 s; `Timeout.InfiniteTimeSpan` turns it off; non-positive refused),
  `HeartbeatComment` (`heartbeat`), `RetryMilliseconds` (a `retry:` frame at the start).

Feeding from Birko.EventBus's `KeyedEventBroadcaster` (TASK-533) needs no glue: subscribe in the handler **before**
returning the result, pass `subscription.Reader`, a mapper from `LiveEvent<T>` (its `Sequence` makes a good `id:`) and the
subscription as `owns`.

## Dependencies
- **Birko.Communication.SSE** (`SseEvent`) and its own dependencies
- **Microsoft.AspNetCore.App** — consumer-supplied, never declared here (a duplicate `FrameworkReference` is NETSDK1087,
  a hard error; TASK-234). A separate project so `Birko.Communication.SSE` stays usable without ASP.NET Core.

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests: `tests/Birko.Communication.SSE.AspNetCore.Tests`.
