---
id: TASK-833
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-832]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# A SQL `ids.Contains(x.Col)` list longer than the provider can bind is split into chunks transparently

## Context

Split from [[TASK-832]] (the user's decision, 2026-11-07). TASK-832 fixed the super-linear cost of an IN-list read:
the diagnostic query built on every read took 6.5 s of a 6.8 s read at 2,000 ids. 2,000 ids now read in 130 ms and
4,000 in 289 ms on SQLite (Release), about 2.2× per doubling. This task is Symbio's second ask (symbio-32, on the
user's word), kept close to its text.

**Ask:** a filtered read whose `ids.Contains(x.Col)` list is longer than the provider can bind, or longer than a chunk
size that keeps it fast, is split by the framework into several IN chunks, and the results are combined. Callers keep
writing one `FindAllAsync(x => ids.Contains(...))`. Symbio does not chunk on its side.

**Points to settle:**
- **The limit counts every parameter in the command,** not only the list's. Known limits:
  - SQL Server allows 2100, so a 2000-id IN plus the tenant and soft-delete parameters is already near it;
  - SQLite allows 32766;
  - PostgreSQL and MySQL have their own limits.
- **Semantics across chunks:**
  - `IN` is a union of the chunks;
  - `NOT IN` is an intersection: the AND of the chunks' NOT INs, never a union;
  - an IN inside an OR or a nested expression is handled, or refused loudly;
  - a `limit`, an `offset` or a sort makes chunking a different query (a global sort and page), so it is refused or
    done correctly. Symbio's batch reads use none of these.
- **Count, delete and update** with the same IN filter get the same treatment, or a stated limitation. Rules 1–3 on
  scope guards apply.
- **MongoDB (`$in`) and the in-memory stores** probably need no chunking. Say so explicitly, per provider.

**Left over from TASK-832's measurement.** Two costs still grow faster than linear, and both are small:
- `AbstractConnectorBase.AddParameter` checks `command.Parameters.Contains(name)` before each add. That is a linear
  scan on SqliteParameterCollection, so rendering 4,000 parameters takes 186 ms.
- SQLite's own binding of named parameters takes 99 ms at 4,000 with no framework involved.

A chunk size caps both. Whether `AddParameter` gets a fast path for names that are unique by construction is this
task's call.

## Acceptance criteria

- [ ] A filtered read whose IN list would exceed the provider's limit (all parameters counted) is split
      transparently and returns exactly the rows the single query would. `IN` is a union and `NOT IN` an intersection,
      each pinned by a test. Per provider, the chunk size and limit are named.
- [ ] An IN under OR or a nested expression, and a chunked read with limit, offset or sort, are either correct or
      refused with an exception that names the reason. They are never silently wrong.
- [ ] Count, delete and update with a chunked IN filter are handled, or refused with a stated limitation. A chunked
      delete or update can never widen its scope (rules 1–3).
- [ ] MongoDB, RavenDB, Cosmos DB, Elasticsearch, InfluxDB and the in-memory and file stores are each stated as
      needing no chunking, or handled, with the reason.
- [ ] SQL Server, which has the lowest limit (2100), is exercised live per `live-tests.yml` with a list past the limit.

## Out of scope

- The super-linear build cost (TASK-832, done).

## Human test plan

N/A — fully covered by automated tests: chunk semantics and per-provider limits are row counts and refusals, which a
machine asserts.

## Implementation plan
