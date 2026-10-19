---
id: TASK-673
# parent deliberately null: no epic owns store-contract additions driven by a consumer import today
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P3
assignee: unassigned
created: 2026-10-19
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL stores have no batch upsert, so an idempotent feed import costs a read per row

## Context

**Filed 2026-10-19 for Symbio TASK-784** ("Bulk, idempotent product + offer upsert for feed importers", post-v1).
That task carries Affiliate's move onto Symbio (Affiliate EPIC-002). Affiliate imports hundreds of thousands of offers
a day, today in bulk writes of 1000 into ElasticSearch. TASK-784 asks for this:

- One call upserts a batch of 1000 or more, matched by an **importer-supplied external id**.
- Re-sending the same batch changes nothing.
- Each bad row is reported on its own; one bad row does not fail the batch.
- No N+1 queries.

**Measured 2026-10-19:**

- No `Birko.Data.SQL*` project emits `ON CONFLICT`, `MERGE`, `ON DUPLICATE KEY` or `INSERT OR REPLACE`.
- `IBulkStore` / `IAsyncBulkStore` have no upsert member.
- Some non-SQL stores upsert internally (Cosmos DB, MongoDB, the JSON stores), but nothing exposes it as a contract.

So on PostgreSQL, Symbio's production provider, an idempotent import today means one of two things:

- read the batch's keys and split it into creates and updates, which is two round trips plus a diff in the caller; or
- write row by row.

## Open questions (decide when picked, with Symbio TASK-784's plan in hand)

1. **Is this framework work at all?** TASK-784 may be served inside Symbio by "read keys, then bulk create plus bulk
   update". This task exists so that answer is given on purpose, not by default. If Symbio solves it locally, cancel
   this task with the measured throughput as the reason.
2. **Key.** Is the conflict target the entity Guid, or a declared unique key such as an external id? The unique key
   needs the index/unique declarations (TASK-628, `__birko_indexes`) to name it, and per rule 58 a key that covers
   only some rows must be declared.
3. **Semantics per provider.**
   - PostgreSQL and SQLite: `ON CONFLICT (...) DO UPDATE`.
   - MySQL: `ON DUPLICATE KEY UPDATE`.
   - SQL Server: `MERGE`, with its known concurrency caveats.
   - A provider that cannot express it refuses (rule 41); it never quietly falls back to row by row.
4. **"Unchanged" detection.** Does a re-send of identical rows count as no write? Compare this repo's TASK-453 ("Decide
   whether an unchanged ViewModel save should skip the write"), not Symbio's TASK-453.
5. **Per-row errors.** A single multi-row statement fails as a whole. Per-row reporting means batching with a
   fallback, or validating before the write. Price it on the steady state (rule 10).
6. **Decorators.** Timestamp, audit, tenant, soft-delete and slug decorators must see an upsert as a create or an
   update per row. Otherwise it becomes a path around their guards (rules 12 and 39).

## Acceptance criteria (if it proceeds)

- [ ] `UpsertAsync(IEnumerable<T>, key)` (name and shape decided) on the bulk store contract, native on PostgreSQL,
      SQLite, MySQL and SQL Server; refused where a provider cannot keep the semantics.
- [ ] Re-sending a batch is idempotent, measured on a live PostgreSQL.
- [ ] Tenant, timestamp, audit and soft-delete decorators apply per row; a test per decorator.
- [ ] A throughput figure for 1000 rows against the read-then-split baseline, recorded here.

## Out of scope

- Cross-merchant dedup and the "sync complete" sweep. Both stay in Symbio / the importer.

## Implementation plan

_Populated when picked up._
