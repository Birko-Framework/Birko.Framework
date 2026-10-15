---
id: TASK-567
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [FIELD-029]
pr: null
github-issue: null
jira-key: null
---

# A SQL table for a model with no declared key accepts a second row with the same Guid — create never refuses it

## Context

Spawned from [[TASK-556]] (2026-10-11), which made bulk create keep a caller-supplied Guid on every provider (CR-L190
reversed). Measured on SQLite with a model that declares no `[PrimaryField]` — the ordinary `AbstractModel`:

```
store.Create(new Row { Guid = g, Name = "first" });       // ok
store.Create(new Row { Guid = g, Name = "single-dup" });  // no throw
store.Create(new[] { new Row { Guid = g, Name = "bulk-dup" } }); // no throw
store.Read(x => x.Guid == g)  → 3 rows: first, single-dup, bulk-dup
```

The generated table is `CREATE TABLE "Rows" (Name TEXT, Guid TEXT)` — **no primary key and no unique constraint on
`Guid`**, although every read, update and delete treats `Guid` as the identity (`DataBase.GetPrimaryFields` falls back to
it). So a create of a Guid that is already stored silently duplicates the row, and later single-row operations act on an
ambiguous key. The in-memory store refuses it (`StoreException`, SH-M310 / [[TASK-163]]); MongoDB (`_id`), Cosmos (`id`)
and Elasticsearch (`Create` op) reject it natively; RavenDB's `Store` overwrites.

Single create always had this. TASK-556 widened the reach: before it, SQL **bulk** create overwrote every caller Guid, so a
bulk batch could not collide — now it can, exactly like a single create.

## Decision (2026-10-12, by the user)

- **A SQL model that declares no key gets `Guid` as its `PRIMARY KEY`** — the identity every read, update and delete
  already assumes (`DataBase.GetPrimaryFields` falls back to it). Not a bare `UNIQUE`: a table with a unique index and no
  key is a shape few tools expect.
- **Existing key-less tables are reported, never altered.** `DetectDrift` reports the missing key; the CHANGELOG gives
  the migration (remove duplicate rows, then add the key). No ALTER on `Init`, which would alter production schema
  unasked and fail on a table that already holds duplicates.
- **RavenDB refuses too.** Create stores with an empty change vector so an existing id is rejected, and throws the same
  `StoreException` as the in-memory store and the SQL providers. Every provider means the same thing by Create.

The task is no longer a decision task: it is runnable by [[fix-next]].

## Acceptance criteria

- [ ] Decide how a SQL table gets a unique identity when the model declares no key: `Guid` as `PRIMARY KEY`, or a
      `UNIQUE` constraint on it — and how existing tables are handled (DetectDrift reports, migration documented; never a
      silent ALTER)
- [ ] A create (single and bulk) of a stored Guid is refused on every SQL provider, with a typed exception consistent with
      the in-memory store; RavenDB's overwrite on create is decided and documented in the same pass
- [ ] Tests on the shared store contract (`tests/Shared/StoreContract/`) prove a duplicate create is refused on every
      provider, red-verified
- [ ] CHANGELOG migration entry; `/specs regen store-crud-contract` and `schema-index-and-ddl`

## Out of scope

- Identity assignment itself — done in [[TASK-556]].

## Human test plan

N/A — library code; covered by the shared contract tests.

## Implementation plan

Drafted 2026-10-15 (queued after TASK-621). The design is the 2026-10-12 decision above; this is the order of work.

1. **Measure first, on the shared contract.** Add `Contract_Create_of_a_stored_guid_is_refused` (single, then bulk) to
   `tests/Shared/StoreContract/IdentityContract.cs`, expecting `StoreException`. Run it on all 12 providers that link
   the contract (SQLite, PostgreSQL, MySQL, MSSQL, TimescaleDB, MongoDB, CosmosDB, ElasticSearch, RavenDB, InMemory,
   JSON, XML) and record each result. The task names only SQL and RavenDB as accepting a duplicate. **JSON and XML are
   unmeasured.** If either upserts, its fix is in scope ("every provider means the same thing by Create"), recorded
   here before it is fixed.
2. **The key, declared once.** In `DataBase.LoadTable`, a table whose model declares no `[PrimaryField]` and has the
   `AbstractModel` `Guid` gets that field's `IsPrimary = true`. Then the DDL (`UsesInlinePrimaryConstraint` → inline
   `PRIMARY KEY`), `GetPrimaryFields`, the update/delete `WHERE` and the TASK-620 drift skip all see one key, and the
   `GetPrimaryFields` fallback becomes unreachable. Delete it rather than keep it (rule 53), after checking its other
   callers. Check `EnsureColumns`' refusal of a key column and the MSSQL unlengthed-key rule (TASK-257) still hold.
3. **Refuse a duplicate create with a typed exception.** The four connectors already classify unique violations
   (`IsUniqueViolationException`). Single and bulk create (sync and async, the SQL bulk tier included, which is a
   separate hierarchy, CLAUDE.md § SQL Stores) catch exactly that classification and rethrow `StoreException` (the
   in-memory store's type), with the inner exception kept. A bulk create that fails part-way: measure whether each
   provider rolls the batch back, and record it.
4. **RavenDB** refuses too: `Store(entity, changeVector: string.Empty, id)` makes an existing id a
   `ConcurrencyException` at `SaveChanges`. Rethrow as `StoreException`, sync and async, single and bulk.
5. **Existing tables are reported, never altered.** The catalogue queries gain a `birko_primary` column (the TASK-620
   `birko_nullable` mechanism: PostgreSQL `pg_index.indisprimary`, MSSQL `sys.index_columns` on the PK index, MySQL
   `COLUMN_KEY = 'PRI'`, SQLite `pk > 0`). `DetectDrift` reports a declared key column the table does not key as a
   new `ColumnDriftKind.KeyMismatch`, plus the reverse. `EnsureColumns` does not act on it.
6. **Docs.** CHANGELOG migration: find duplicate Guids (`SELECT Guid, COUNT(*) … HAVING COUNT(*) > 1`), resolve them,
   then add the key, with the statement per provider. SQLite has no `ALTER TABLE … ADD PRIMARY KEY`, so it needs the
   rebuild recipe. Recent Updates entry (BREAKING: new tables gain a key). Then `/specs regen store-crud-contract` and
   `schema-index-and-ddl`.
7. **Tests.** The contract test, red on SQL and RavenDB first (and wherever step 1 finds more). Live drift tests for
   `KeyMismatch` per SQL provider. The existing suites, unchanged in meaning: a `GetPrimaryFields` fallback test, if
   one exists, is inverted, not deleted (rule 56).

Containers: PostgreSQL, MySQL, MSSQL, TimescaleDB, MongoDB (replica set), CosmosDB emulator, ElasticSearch and RavenDB,
per live-tests.yml.
