---
id: TASK-1003
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-812]
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB and MongoDB aggregates answer empty on an unconfigured store

## Context

Found at the 2026-11-27 `/specs regen` of `backend-store-implementations`; the user asked for it to be filed.

Per the [[TASK-812]] rule, every other read of a store with no client refuses rather than answering. Rule 26 says the
same: a reader that answers an error with an empty result gives a wrong answer. The aggregates of two backends still
answer empty:

- `Birko.Data.InfluxDB/Stores/AsyncInfluxDBStore.cs:819`: `if (Client == null || _settings == null) return Array.Empty<AggregateResult>();`
- `Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs:632`: `if (Collection == null) return Array.Empty<AggregateResult>();`
- The sync twins, and `Birko.Data.MongoDB/Aggregation/StoreAggregationHelper.cs:416` (aggregates over an empty set).
  Check whether that path is reachable the same way.

So a misconfigured store reports "no data" from an aggregate, while its `Read` and `Count` refuse.

## Acceptance criteria

- [x] Red first: an aggregate on an unconfigured InfluxDB store and on an unconfigured MongoDB store (sync and async)
  answers empty today.
- [x] Both refuse, as their other reads do in that state: the same exception type and message shape as TASK-812.
- [x] Any other aggregate on these backends that answers empty for "not configured" is found and handled the same way.
- [ ] The `backend-store-implementations` spec is regenerated. Not done here: it waits for the next scoped `/specs regen` of
  that area, which also takes TASK-1007.

## Evidence 2026-11-27

The shared `UnconfiguredContract` (TASK-812) now also calls `Aggregate` / `AggregateAsync` with a Count over `Guid`, on
every backend that runs it. InfluxDB, which does not run it, got the same call in its own `Task812UnconfiguredTests`.

**Before the fix, it failed on:**
- InfluxDB, sync and async (as filed);
- MongoDB, sync and async (as filed);
- **the async JSON and XML stores**, which the harvest did not name. Their `AggregateAsync` called only
  `EnsureDataLoadedAsync`, so an unconfigured store answered an empty list. The sync twins pass the gate.

RavenDB, Cosmos DB, Elasticsearch, the four SQL providers, TimescaleDB and InMemory already refused.

**Fix.** The six aggregates call `EnsureInitializedAsync` / `EnsureInitialized`, the TASK-812 gate, in place of the empty
answer. The message and exception are those of every other read.

**After the fix:**
- The unconfigured tests pass on all 13 backends, live.
- The full suites pass: InfluxDB 88, MongoDB 535, JSON 450, XML 535.
- The 18 other test projects that import those four pass, live (StoreFactory 26 with every NoSQL server up).

**Checked, not a defect:** `StoreAggregationHelper.ReadResults`' empty branch runs only after a query that returned no
documents. It answers an ungrouped aggregate over nothing with its one row (count 0), as the in-memory aggregation does.

**Left as is (rule 53, not a wrong answer):** RavenDB, Cosmos DB and the SQL base stores keep an `if (… == null) return
empty` line after their gate, so nothing reaches it.

## Out of scope

- The other backends' aggregates. They already refuse, per the spec.

## Human test plan

N/A — fully covered by automated tests.
