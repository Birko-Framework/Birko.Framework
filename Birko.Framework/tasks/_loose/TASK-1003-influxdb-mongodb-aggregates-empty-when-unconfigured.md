---
id: TASK-1003
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Red first: an aggregate on an unconfigured InfluxDB store and on an unconfigured MongoDB store (sync and async)
  answers empty today.
- [ ] Both refuse, as their other reads do in that state: the same exception type and message shape as TASK-812.
- [ ] Any other aggregate on these backends that answers empty for "not configured" is found and handled the same way.
- [ ] The `backend-store-implementations` spec is regenerated.

## Out of scope

- The other backends' aggregates. They already refuse, per the spec.

## Human test plan

N/A — fully covered by automated tests.
