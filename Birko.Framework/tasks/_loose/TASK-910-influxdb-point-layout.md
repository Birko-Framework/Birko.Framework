---
id: TASK-910
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-908]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB: how the store lays out points (series per point, write-time timestamps)

## Context

Filed at the user's request (2026-11-11), from TASK-907's measurement:
- `InfluxDBStore` / `AsyncInfluxDBStore.ModelToPoint` writes every entity as one point tagged `Guid=<entity id>`, with
  every other property as a field. A tag is part of the series key, so **every entity is its own series**: reading
  or aggregating N entities makes InfluxDB stitch N series back together (pivot). Measured at 100k points: reading
  takes 45–50 s; a server-side Flux aggregate 46–49 s; aggregating the read rows in memory 51–79 ms. High series
  cardinality is also InfluxDB's documented main cost driver for memory and index size.
- The point timestamp is `DateTime.UtcNow` at write time, not a property of the entity (TASK-906 found time buckets
  grouped on write time).
- **Consumer:** Symbio.Affiliate's `Affiliate.Import` stores import/synchronization logs and stats in InfluxDB
  (`ImportErrorLogStore`, `SynchronizationLogStore`, `SynchronizationProgressLogStore`, `Stats`) — existing data was
  written in the current layout.

Options to weigh (not decided): the entity id as a field instead of a tag, with chosen low-cardinality properties
declared as tags (e.g. an attribute); a declared timestamp property; how reads, updates and deletes by id work without
the tag (Flux delete predicates accept only tags and time); migration or dual-read of existing data.

Native Flux aggregation (TASK-908) is only worth re-measuring after this change: with few series it may become faster
than reading everything.

## Acceptance criteria

- [ ] Measure the current layout's cost (reads, updates/deletes by id, aggregates; 10k/100k/1M points) and the
      candidate layouts' (rule 54).
- [ ] The user decides the layout and the migration path for existing data (Symbio.Affiliate).
- [ ] Implement with tests red before; re-measure native Flux aggregation and record the decision.

## Out of scope

- The configurable read timeout (TASK-908).

## Human test plan

- Run Symbio.Affiliate's import against an InfluxDB holding data written in the old layout; confirm its logs and
  stats still read (or migrate as decided).

## Implementation plan
