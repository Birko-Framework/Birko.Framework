---
id: TASK-1000
parent: EPIC-019
feature: null
status: todo
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

## Open decisions (settle with the user before building)

- **Where the marker lives:**
  - (a) a flag on the batch's last event: no extra document, but readers must see the whole batch to judge it;
  - (b) a separate marker document per batch: one more write per multi-event append.
- **Who repairs an orphaned batch:**
  - (a) the next append to the aggregate: it must delete the orphans first, or its version would follow them;
  - (b) reads only filter the orphans, which are never deleted;
  - (c) an explicit repair call.
- **Cost:** single-event appends (the common case) should stay one write. Measure the read overhead of filtering
  uncommitted batches on Elasticsearch.
- **Scope:** only the two backends above, or the default store generally (any future backend without transactions).

## Acceptance criteria

- [ ] Decisions recorded.
- [ ] A crash between a batch's writes (simulated by a store that fails after N writes, the process "dying" before
      compensation) leaves no event of that batch visible to `Read*` or `GetVersion`. Shown failing first on Elasticsearch
      and standalone MongoDB.
- [ ] Single-event appends cost the same as today (measured).
- [ ] The shared event store contract still passes on every backend; READMEs updated (the gap note removed where it is
      closed).

## Out of scope

- Backends that are already atomic.

## Human test plan

N/A — fully covered by automated tests (live suite).
