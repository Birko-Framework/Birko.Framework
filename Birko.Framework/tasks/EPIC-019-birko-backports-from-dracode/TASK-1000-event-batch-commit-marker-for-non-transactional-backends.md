---
id: TASK-1000
parent: EPIC-019
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-27
depends-on: [TASK-995]
blocks: []
related: [TASK-995]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A batch of events stays all-or-nothing across a crash on backends without transactions

## Context

Asked for by the user (2026-11-27), after [[TASK-995]]. The default event store (`AsyncStoreEventStore`) appends a batch
one event at a time and, on a conflict, deletes the events it already wrote. A process that dies between the two leaves
the batch's first events stored: valid, contiguous versions, but only part of one command. The TASK-995 decision 4 gap
is documented in the READMEs.

Backends where this applies today:
- **Elasticsearch** (`ElasticSearchEventStore`, the default plus a refresh);
- **MongoDB on a standalone server** (`MongoDBEventStore` falls back to the default);
- **InMemory** (single process, so a crash loses everything anyway; not a target).

SQL, RavenDB, Cosmos DB and MongoDB on a replica set are atomic natively.

**The approach the user asked about: a "batch complete" marker.** Each event of a multi-event batch carries the batch's
id, and the batch is committed by a final marker write. Readers treat events of an uncommitted batch as absent, and a
later writer (or a repair pass) removes them.

## Decisions (the user's, 2026-11-27)

Asked as open questions; the user chose the recommended option of each.


- **Where the marker lives: (a) the batch's last event.** Each record of the batch carries the batch id, its size and
  the id of its last event; the batch counts once all its records exist. No extra document. (b), a separate marker
  document per batch, was not chosen.
- **Who repairs an orphaned batch: (a) the next append, after a lease.** Reads hide an incomplete batch; an append that
  collides with one older than the lease clears it and retries, and a younger one is a conflict (it may be a live
  writer). (b) alone was rejected: the orphan holds its versions, so the aggregate would be stuck. (c) was not chosen;
  no explicit repair call was built.
- **Lease: 30 s, configurable** (`AbandonedBatchLease`). 5 minutes was the other option.
- **Cost:** single-event appends stay one write (a requirement, measured below).
- **Scope: the default store generally**, so any backend without transactions gets it; the atomic overrides are
  unchanged.

## Acceptance criteria

- [x] Decisions recorded.
- [x] A crash between a batch's writes (simulated by a store that fails after N writes, the process "dying" before
      compensation) leaves no event of that batch visible to `Read*` or `GetVersion`. Shown failing first on Elasticsearch
      and standalone MongoDB.
- [x] Single-event appends cost the same as today (measured).
- [x] The shared event store contract still passes on every backend; READMEs updated (the gap note removed where it is
      closed).

## Done 2026-11-27

**Mechanism** (`AsyncStoreEventStore`, `EventRecord`):
- **New fields:** `EventRecord` gains `BatchGuid`, `BatchSize`, `BatchEnd`, `WrittenAt` and `Aborted`. They are null or
  0 on single events, on atomic overrides and on older records, so existing data reads as before.
- **Reads:** `VisibleAsync` hides an incomplete batch and abort markers. `GetVersion` walks down from the top of the stream
  and stops at the first visible record.
- **Clearing an abandoned batch:** an append that collides with an incomplete batch past the lease first creates an
  abort marker under the batch's last event id. That create and the batch writer's own last create exclude each other,
  so a batch is either completed or cleared, never both. The batch is then deleted by its batch id.
- **Rollback:** a failed batch removes its own records by batch id, so it cannot delete a version another writer has
  since taken. After its last create, a writer checks that its batch is whole, and fails rather than report success
  for a batch that was cleared under it.
- **Elasticsearch:** `MakeVisibleAsync` refreshes the index before these checks, because they are searches.
- **Clocks:** the lease is judged from the writers' clocks, as documented in the code. Clocks that differ by more than
  the lease can clear a live batch; its writer then fails rather than reports success.

**Tests:**
- `tests/Shared/EventSourcing/CrashedBatchContract.cs` has 6 tests. The writer's store dies after N creates, and its
  rollback dies with it. The suite runs on InMemory, live on Elasticsearch, and live on standalone MongoDB.
- **Shown failing first:** on the old append code, all three backends read `{1, 2, 3}` where `{1}` was expected, so the
  cut batch's first two events were visible.
- **InMemory races:** a writer stalled past the lease whose batch is cleared fails and leaves nothing. A writer whose
  last event meets an abort marker fails. A batch left with a marker (writer and clearer both dead) is cleared at once,
  whatever the lease. A negative lease is refused.
- **Cost:** `BatchMarkingCostTests` pins it: a single event is 1 create and 0 reads, a 4-event batch is 4 creates plus
  1 check, and reading whole batches costs one query.
- **Every suite passes:**

  | Event store suite | Passed |
  |---|---|
  | InMemory | 54/54 |
  | Elasticsearch (live) | 16/16 |
  | MongoDB, standalone and replica set (live) | 27/27 |
  | RavenDB (live) | 10/10 |
  | Cosmos DB (live) | 13/13 |
  | SQL | 37/37 (SQLite ran; the server tests skipped) |

- **Consumers:** Symbio.Birko and DraCode.Birko build against the working tree.

**Measured on live Elasticsearch, old code against new, 3 runs each (ms per call):**

| | old | new |
|---|---|---|
| single-event append | 18–21 | 18–20 |
| 5-event batch append | 42–46 | 59–62 (one refresh and one check more) |
| read 100 events in batches of 5 | 6.4–7.4 | 7.4–7.8 |
| read up to a version that cuts a batch | 7.1–7.6 | 12–14 (one lookup for the cut batch) |
| current version, batched stream | 3.9–4.3 | 3.5–4.2 |

The first version of `GetVersion` judged every batch in the 16 records it read, at 9–14 ms on a batched stream. It now
stops at the first visible record.

**Docs:**
- The gap notes are replaced in the Birko.Data.EventSourcing CLAUDE.md and README, in the Elasticsearch, MongoDB and
  RavenDB project docs, in `docs/event-sourcing.md`, and in the three test project docs.
- A Recent Updates entry is added. The oldest entry (2026-11-11, TASK-919) was rolled into CHANGELOG.md.

## Out of scope

- Backends that are already atomic.

## Human test plan

N/A — fully covered by automated tests (live suite).
