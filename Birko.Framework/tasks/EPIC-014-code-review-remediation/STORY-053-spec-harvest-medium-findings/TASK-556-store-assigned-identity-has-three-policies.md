---
id: TASK-556
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-10-11
depends-on: []
blocks: []
findings: [SH-M313, SH-M314]
pr: null
github-issue: null
jira-key: null
---

# Store-assigned identity follows three different policies across backends — one of them overwrites the caller's Guid

## Context

Spawned from [[TASK-163]] (2026-10-11) while verifying `SH-M313` / `SH-M314`. Those two were filed against the
in-memory store (`Guid.Empty` survives `data.Guid ??= Guid.NewGuid()`, is stored under the empty key, and
`CreateCore` then returns `Guid.Empty` — the same value it returns for `null` data; the `Read(Guid)` override then
refuses `Guid.Empty`, so the row is unreachable by id). **Confirmed, and the shared root cause is wider:** "give
this entity an identity" has no single producer (rule 16), and the ~55 sites that do it follow three policies:

| Policy | Where (measured 2026-10-11) |
|---|---|
| Assign only when `null` — **`Guid.Empty` is kept as the id** | `??=` in 40 sites: in-memory, JSON, XML, SQL single-item `CreateCore` (`DataBaseStore.cs:197`, `AsyncDataBaseStore.cs:265`), MongoDB, RavenDB single-item, CosmosDB, ElasticSearch single-item, InfluxDB, EventSourcing wrappers, ViewModel repositories |
| Assign when `null` **or** `Guid.Empty` | ElasticSearch bulk (`ElasticSearchStore.cs:232`), CosmosDB async (`AsyncCosmosDBStore.cs:329`) |
| **Always overwrite**, even a caller-supplied Guid | SQL bulk create on every provider (`SQLiteStore.cs:47`, `MSSqlStore.cs:68`, `MySQLStore.cs:62`, `PostgreSQLStore.cs:65` + async twins), RavenDB bulk (`RavenDBStore.cs:399,412`, async `:452,468`) |

The third row is the serious one: a caller that sets Guids to link entities (parent/child, idempotent import) and
bulk-creates them gets **different** ids stored on the framework's default provider — and the same call keeps the ids
on JSON / in-memory, so tests pass. ⚠ **That row is a recorded decision, not an accident:** `PostgreSQLStore.cs:61-64`
cites CR-L190 ("bulk create assigns a fresh server-side Guid to every row, intentionally discarding any caller-supplied
Guid … use the single-item path to preserve a known id"). CR-L190 aligned the SQL providers with *each other*; it did
not look at the portable backends or at RavenDB's bulk path, which keep or overwrite differently. So this task
**reopens CR-L190 on the record** — keep it (and make every backend's bulk create overwrite, documented on
`IBulkStore.Create`) or reverse it — rather than quietly changing it.

`AbstractStore.Save` already treats `Guid.Empty` as "new" (`AbstractStore.cs:239`), which is the policy the first row
contradicts: `Save(entity with Guid.Empty)` → `Create` → stored under `Guid.Empty`.

## Decision (2026-10-11, by the user)

**CR-L190 is reversed: bulk create keeps a caller-supplied Guid on every backend**, exactly as single-item create
does. One producer assigns an identity only when the Guid is `null` or `Guid.Empty`; every site uses it. A duplicate
id on bulk create fails as a single create does. CHANGELOG migration note for SQL / RavenDB bulk callers who relied on
fresh server-side ids.

The task is no longer a decision task: it is runnable by [[fix-next]].

## Acceptance criteria

- [x] One producer — e.g. `AbstractModel.EnsureGuid()` (assign when `null` or `Guid.Empty`, keep any other value) —
      used at every site above; no store re-implements the rule
- [x] CR-L190 decided on the record: bulk create either keeps a caller-supplied Guid on **every** backend (SQL and
      RavenDB then can hit a duplicate key — test it, consistently with [[TASK-163]]'s in-memory "create never
      replaces") or overwrites it on every backend, documented on `IBulkStore.Create` / `IAsyncBulkStore.CreateAsync`
- [x] In-memory `Read(Guid)` and the base `Read(Guid)` agree on `Guid.Empty` once nothing can be stored under it
- [x] A per-provider contract test: create with `null`, with `Guid.Empty`, and with a caller Guid (single and bulk);
      red-verified, including the bulk-overwrite case on SQLite
- [x] CHANGELOG migration entry for the bulk-create behaviour change
- [x] `/specs regen store-crud-contract`

## Out of scope

- `Update` / `Save` of an entity that is not stored — [[TASK-555]].

## Human test plan

N/A — a library contract with no UI surface; fully covered by the per-provider contract tests.

## Implementation plan

_Populated by `/tasks plan TASK-556` — leave empty until then._

## Outcome

**Fixed.** Every store now gives an entity its identity the same way: a fresh Guid when it has none or has
`Guid.Empty`, and the caller's own Guid kept otherwise — single or bulk, on every provider. Bulk create on SQL,
TimescaleDB and RavenDB used to throw the caller's ids away and store entities under new ones; it now keeps them, as the
user decided (2026-10-11, CR-L190 reversed).

- **One producer:** `AbstractModel.EnsureGuid()`. 61 sites in 40 files now call it: the `??=` form (kept `Guid.Empty`),
  the guarded null-or-empty form, the null-only `HasValue` form (JSON / XML async bulk), the unconditional overwrite
  (SQL ×8, TimescaleDB ×2, RavenDB bulk ×4), and a **fourth form the first sweep missed** —
  `item.Guid = item.Guid ?? Guid.NewGuid()` in MongoDB and InfluxDB bulk create — which the shared contract caught live on
  MongoDB. Background-job and sync model mappers (`Guid ?? Guid.NewGuid()` when converting a domain object) are not
  store-assigned identity and were left.
- **Documented** on `IBulkCreateStore.Create` / `IAsyncBulkCreateStore.CreateAsync`; stale CR-L190 comments
  (PostgreSQL) and the ViewModel repositories' comment ("every store honours a pre-assigned Guid, because every one uses
  `??=`" — never true of SQL/RavenDB bulk) rewritten.
- **Read(Guid.Empty):** the in-memory override refuses it and the base filters on it; with nothing stored under
  `Guid.Empty` they agree — pinned by the contract (`…empty_guid…` asserts a read of `Guid.Empty` finds nothing).
- **Contract:** `tests/Shared/StoreContract/IdentityContract.cs` (5 sync + 4 async), same 12 test projects; the shared
  classes were renamed `StoreContract<T>` / `AsyncStoreContract<T>` (partial) since they now hold more than update-miss.
- **Step-6 split (store edits reverted, `EnsureGuid` kept), identity tests red:** InMemory, JSON, SQLite, PostgreSQL,
  MySQL, SQL Server, TimescaleDB, MongoDB, RavenDB, CosmosDB 4/11 each; XML 12/33 (3 layouts); Elasticsearch 2/11 (its
  bulk path already treated Empty as new). **Contract pins** (green on revert): create with no Guid, single create keeps a
  caller Guid (sync + async).
- **Live (throwaway containers, removed):** PostgreSQL 136, MySQL 158, SQL Server 172, TimescaleDB 77, MongoDB 127,
  RavenDB 90, Elasticsearch 207, CosmosDB 84 — all green. Local: InMemory 117, JSON 58, XML 95, SQLite 481. Full solution
  (no live servers): 169/169 test projects green.
- **Measured, spawned [[TASK-567]] (acceptance 2's duplicate-key half):** a SQL table for a model with no declared key has
  no PK/unique on `Guid`, so a create of a stored Guid inserts a second row — single (as always) and now bulk too (SQLite:
  3 rows under one Guid). Fixing it is a DDL + migration decision, so it is its own task; the interface docs say so.
- **Process incident, recorded:** this task's step-6 revert list was built from `git diff` globs and swept in another live
  session's uncommitted `Birko.EventBus/Local/InProcessEventBus.cs`; that session's TASK-565 commit `829034b9` landed in
  the revert window without its production fix. Told the session (it re-committed as `d622f1c4`) and the user; feedback
  memory added (revert and commit only explicit own paths). This commit uses an explicit pathspec.
- **Merge gate:** inline review. Public surface: new `AbstractModel.EnsureGuid()`. No nullable warnings.

## Progress log

- step 2 — picked; ranked above TASK-557 (delegate return discarded, in-memory live references) on key 1: SQL/RavenDB bulk create stores different ids than the caller set (wrong data, breaks pre-linked entities) on the default provider; decided 2026-10-11 (4e6ef5f2): keep the caller Guid everywhere, one producer for identity assignment
- step 3 — verified: held as written and wider — a fourth assignment form (`x.Guid = x.Guid ?? Guid.NewGuid()`, MongoDB/InfluxDB bulk) found by the live contract
- step 4 — layer: local; decision recorded 2026-10-11 (4e6ef5f2): keep the caller Guid everywhere
- step 5 — `AbstractModel.EnsureGuid()` + 61 call sites in 40 files; interface docs; `IdentityContract` (9) on 12 providers; all local + 8 live suites green; full solution 169/169
- step 6 — store edits reverted → identity tests red on every provider (numbers in Outcome)
- step 7 — respecced store-crud-contract: new requirement *Every store assigns an identity through one rule, single or bulk* (2 scenarios); in-memory create requirement amended, its Guid.Empty scenario inverted, the null-create scenario corrected
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
