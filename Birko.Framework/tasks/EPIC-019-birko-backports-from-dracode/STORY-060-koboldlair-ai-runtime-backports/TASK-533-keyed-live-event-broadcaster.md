---
id: TASK-533
parent: STORY-060
feature: null
status: todo
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

- [ ] Generic over `TKey` / `TEvent`, in `Birko.EventBus` (or where CLAUDE-projects.md § Dependency Flow puts it)
- [ ] A dropped event is visible to the subscriber as a sequence gap, not silent
- [ ] Completing a key completes its subscribers' readers
- [ ] Tests: a slow subscriber blocks neither publish nor other subscribers; ordering; subscribe-before-publish; completion
- [ ] Usable as the source of the SSE result in [[TASK-540]]

## Out of scope

- `RunRegistry` (folding the stream into run status) — DraCode policy

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
