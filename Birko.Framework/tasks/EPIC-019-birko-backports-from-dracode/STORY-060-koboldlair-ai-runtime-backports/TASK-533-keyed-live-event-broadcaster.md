---
id: TASK-533
parent: STORY-060
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A keyed live-event broadcaster: non-blocking publish, drop-oldest per subscriber, per-key sequence

## Context

DraCode's `Services/KoboldRunEventSource.cs` (124) fans each key's events out to subscriber channels. `Publish` is
synchronous and never blocks; each subscriber gets a bounded `DropOldest` channel; a `Sequence` is stamped per key; a
subscriber can attach before the run starts; `CompleteRun` cleans up. `InProcessEventBus` awaits handlers one by one and
routes by event type, which DraCode TASK-037 records as unsuitable for streaming to SSE and WebSocket clients.

Adopted in the consumer by DraCode TASK-131 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] Generic over `TKey` / `TEvent`, in `Birko.EventBus` (or where CLAUDE-projects.md § Dependency Flow puts it)
- [x] A dropped event is visible to the subscriber as a sequence gap, not silent
- [x] Completing a key completes its subscribers' readers
- [x] Tests: a slow subscriber blocks neither publish nor other subscribers; ordering; subscribe-before-publish; completion
- [x] Usable as the source of the SSE result in [[TASK-540]]

## Out of scope

- `RunRegistry` (folding the stream into run status) — DraCode policy

## Human test plan

N/A — library behaviour, covered by unit tests.

## Resolution (2026-10-17)

`KeyedEventBroadcaster<TKey, TEvent>` in `Birko.EventBus/Live` (no new dependency: `System.Threading.Channels` is in
the BCL), with `LiveEvent<TEvent>(Sequence, Event)` and `LiveEventSubscription<TEvent>` (`Reader`, `ReadAllAsync`,
`Dropped`, `Dispose`).

Backported from `KoboldRunEventSource`, with its gaps closed:
- **Ordering.** DraCode stamped the sequence under the lock and wrote to the channels outside it, so two concurrent
  publishers could deliver 6 before 5, which also reads as a false gap. Now both happen under the key's lock; a bounded
  DropOldest `TryWrite` never waits. Re-applying DraCode's pattern as a mutation fails
  `Concurrent_publishers_never_reorder_a_subscribers_sequence` 3/3.
- **A drop is visible.** It shows as a sequence gap, as before, and is also counted per subscriber (`Dropped`, through
  the channel's `itemDropped` callback).
- **No leak.** An event for an unwatched key creates nothing, and a key is forgotten when its last subscriber leaves.
  DraCode kept an entry created by a subscriber whose run was never completed. A forgotten key's sequence restarts at 1.
- `Complete(key, error)` passes a failure to the readers after their buffered events.

**Tests:** 9 in `KeyedEventBroadcasterTests`: subscribe-before-publish; a slow subscriber at capacity 4 under 1000 events
(the publisher is not slowed, the fast reader gets all 1000 in order, the slow one reads 997..1000 with `Dropped = 996`);
8 × 2000 concurrent publishers in order; completion with and without an error; an unwatched key; detach and forget;
independent keys; publish racing complete and dispose; capacity validation. Stable over 3 repeats. Regression: all
7 importers of `Birko.EventBus.projitems` green (EventBus 122, MessageQueue 7, Outbox 9, Outbox.SQL 17, Tenant 26,
Rules 191, Security.AspNetCore 113). The SSE result of [[TASK-540]] can read `ReadAllAsync`.
