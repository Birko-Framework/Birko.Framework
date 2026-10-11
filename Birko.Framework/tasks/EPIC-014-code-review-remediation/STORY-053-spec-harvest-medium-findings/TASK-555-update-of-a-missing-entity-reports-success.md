---
id: TASK-555
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-10-11
depends-on: []
blocks: []
findings: [SH-M306, SH-M309]
pr: null
github-issue: null
jira-key: null
---

# `Update` of an entity that is not stored reports success on every backend, and `Save` hands back its Guid

## Context

Spawned from [[TASK-163]] (2026-10-11) while verifying `SH-M306` / `SH-M309`. Both were filed against the in-memory
store; **both are confirmed, and broader than filed**: nothing in the framework can report "no row matched".

- `IUpdateStore<T>.Update` returns `void`. Every backend no-ops on a missing key:
  `AbstractInMemoryStore.UpdateCore` / `AbstractJsonStore.cs:87` / XML (`ContainsKey` guard, nothing written),
  `DataBaseStore.cs:209` (an `UPDATE … WHERE pk = …` that matches 0 rows), `MongoDBStore.cs:163`
  (`ReplaceOne(… IsUpsert = false)`, `MatchedCount` never read).
- `AbstractStore.Save` (`AbstractStore.cs:233`) routes a non-empty Guid to `Update` and returns `data.Guid!.Value`
  (async twin `AbstractAsyncStore.SaveAsync`). So `Save(entityWithClientAssignedGuid)` — a natural way to insert
  an entity whose id the caller chose — writes nothing and returns a plausible identifier.
- The bulk `Update(IEnumerable)` applies partially with no report of which items were skipped.

Not fixed in TASK-163 because a fix is a **contract decision across ~20 providers**, not an in-memory edit: making
the in-memory double strict alone would make it diverge from every real backend it stands in for.

## The decision to take first

1. **`Update` of a missing entity** — throw (`StoreException`, or a typed `EntityNotFoundException`), or return a
   count/bool (a breaking signature change across `IUpdateStore` / `IAsyncUpdateStore` and every wrapper).
2. **`Save` with a Guid that is not stored** — upsert (insert it under the caller's Guid), or throw. Upsert matches
   what `Save` is used for; it needs a per-provider native upsert or a read-then-write inside the store's boundary.

## Decision (2026-10-11, by the user)

1. **`Update` of a missing entity throws** a typed `EntityNotFoundException` (a `StoreException`); `Update` stays
   `void`. Each provider reads its native matched/affected count — no pre-read. The bulk `Update(IEnumerable)` throws
   too, naming the missing ids.
2. **`Save` with a Guid that is not stored upserts** — inserts the entity under the caller's Guid (native upsert where
   the provider has one, otherwise update-then-create-on-miss inside the store's boundary).

The task is no longer a decision task: it is runnable by [[fix-next]].

## Acceptance criteria

- [x] The two decisions above recorded (here, and in `docs/specs/store-crud-contract.md` after regen)
- [x] Every provider implements them — in-memory, JSON, XML, SQL (all four + SQLite), MongoDB, RavenDB, CosmosDB,
      ElasticSearch, InfluxDB — each reading its native matched/affected count rather than a pre-read
- [x] A shared contract test (one fixture, run per provider) proves: `Update(missing)` behaves as decided;
      `Save(new entity with a client Guid)` behaves as decided; bulk `Update` with one missing item does not report
      success silently. Red-verified per provider
- [x] CHANGELOG migration entry (this is observable to every consumer that relied on the silent no-op)
- [x] `/specs regen store-crud-contract`; the "Save returns the Guid of an entity it never stored" scenario changes

## Out of scope

- Identity assignment on create (null / `Guid.Empty` / overwrite) — [[TASK-556]].
- `StoreDataDelegate<T>`'s ignored return value — [[TASK-557]].

## Human test plan

N/A — a library contract with no UI surface; fully covered by the per-provider contract tests.

## Implementation plan

Written by [[fix-next]] at step 4 (2026-10-11). Units are done in order; each is ticked here as it lands so a
reset session resumes at the first unticked one. The whole task is still ONE commit (fix + tests together).

**Seam decision.** Providers throw from their own `UpdateCore` — no signature change. Measured: 0 consumer overrides of
`UpdateCore`, but 54 framework overrides and 39 test doubles; a `bool`-returning seam would touch all 93 for no gain.

**Save decision → one producer.** Measured: the create-or-update routing is re-implemented in ~30 `Save` / `SaveAsync`
bodies (base stores, both repositories, Patterns decorators ×10, Localization ×4, EventSourcing ×2, Tenant ×2,
Telemetry ×2, Validation ×2, Workflow instance stores ×7, MirrorDataSource) — so an upsert in the base `Save` alone
reaches none of the wrappers. One helper owns it; each wrapper passes its own create / update so its decoration still
runs. The store delegate is wrapped run-once, so a miss that falls through to create does not apply it twice.

- [x] A. `Birko.Data.Core/Exceptions/EntityNotFoundException.cs` (`: StoreException`, `EntityType`, `Guids`);
      `Birko.Data.Stores` save helper (sync + async): null/empty Guid → create; else update, and on
      `EntityNotFoundException` → create with the same Guid; delegate run once. Base `Save` / `SaveAsync` use it.
- [x] B. Every wrapper / repository / workflow `Save` in the list above routes through the helper. Native-upsert
      providers (Mongo `SaveAsync`, Cosmos `SaveAsync`) already upsert — verify, leave.
- [x] C. Single `UpdateCore` throws on a miss, reading the native signal: InMemory (`ReplaceExisting` result),
      JSON, XML ×4 variants, SQL base (`DataBaseStore` / `AsyncDataBaseStore` affected rows) + provider overrides
      (SQLite, MSSql, MySQL, PostgreSQL, TimescaleDB), MongoDB (`MatchedCount`), RavenDB, CosmosDB (404),
      ElasticSearch, InfluxDB (decide: a point write has no "missing" — record it).
- [x] D. Bulk `UpdateCore(IEnumerable)` on the same providers: apply what matches, then throw naming every missing id.
- [x] E. Shared contract fixture `tests/Shared/StoreContract/UpdateMissContract.cs` (abstract, linked into each
      provider test project): `Update(missing)` throws `EntityNotFoundException` and writes nothing; `Save(new entity
      with a client Guid)` stores it under that Guid; bulk `Update` with one missing item updates the rest and throws
      naming the missing id; delegate runs once on the upsert path. Local first (InMemory, JSON, XML, SQLite), then
      live via Docker (PostgreSQL, MySQL, MSSql, MongoDB, RavenDB, ElasticSearch; Cosmos emulator if available).
- [x] F. Red-verify per provider (revert that provider's throw → its contract tests red), CHANGELOG migration,
      `/specs regen store-crud-contract`, close.

## Outcome

**Fixed on every provider.** Updating an entity that is not stored now throws `EntityNotFoundException` instead of
quietly doing nothing, and `Save` with a Guid that is not stored now creates the entity under that Guid instead of
returning an identifier for data it never wrote. Both decisions are the user's (2026-10-11, `4e6ef5f2`).

- **One producer for Save.** The create-or-update choice was re-implemented in ~30 `Save` bodies; `StoreSave` now owns
  it, and the base stores plus 20 decorator `Save`s call it with their own create/update, so each decorator's create
  behaviour (timestamps, slug, audit, tenant…) runs on the upsert. Delegate wrapped run-once.
- **Native miss signals, no pre-read:** key lookup (in-memory, JSON, XML ×3 layouts), rows matched (SQL — connector
  `Update(Async)` returns it; `BulkUpdate(Async)` return the unmatched models), `MatchedCount` (MongoDB, acknowledged
  only), the load it already did (RavenDB), item 404 except sub-status 1003 (CosmosDB), `document_missing_exception`
  (Elasticsearch). **InfluxDB — judgement call:** a point write has no matched count and the decision rules out a
  pre-read, so its `Update` stays a write (an upsert) and is documented as such; the contract is not linked there.
- **Found and fixed, all measured (wider than filed):**
  - SQL native `BulkUpdate` / `BulkDelete` (sync + async × SQLite, PostgreSQL, MySQL, SQL Server = 16 sites) keyed on
    `Table.GetPrimaryFields()`, which has no `Guid` fallback — for a model with no `[PrimaryField]` they silently wrote
    / deleted **nothing** (SQLite: 0 of 2 deleted, a rename lost). Now `DataBase.GetPrimaryFields(type)`, the single-item
    paths' lookup; `BulkPrimaryKeyFallbackTests` (3). A TimescaleDB test comment that described the old behaviour updated.
  - RavenDB `Update` stored unconditionally: a missing entity was silently **created**. Same for the async XML bulk update.
  - `AsyncMongoDBStore.SaveAsync` never invoked the delegate on its update branch.
  - `AsyncCosmosDBStore.SaveAsync` overrode the public wrapper without the lazy-init gate: as a store's first call it
    returned `Guid.Empty` and wrote nothing (the slip CR-H077 fixed in RavenDB).
  - CosmosDB bulk update used `Task.WhenAll`, which surfaced only the first failure; misses are now collected per item.
- **Contract:** `tests/Shared/StoreContract/UpdateMissContract.cs` — 6 sync + 6 async tests, linked into 12 test
  projects, with `Ready()` (live providers report SKIPPED, fail under `BIRKO_REQUIRE_LIVE`) and `Settle()` (ES refresh).
  Plus `DecoratorSaveUpsertTests` (10) and `BulkPrimaryKeyFallbackTests` (3).
- **Step-6 split, per provider (contract reverted → red):** InMemory base `Save` 4/12; JSON/XML via base; SQLite 9/15
  (incl. key fallback); PostgreSQL, MySQL, SQL Server, TimescaleDB 6/12 each (live); MongoDB 6/12 (live; incl. the
  delegate fix); RavenDB 4/12 (its Save already upserted); Elasticsearch 8/12; CosmosDB 8/12 (live emulator);
  decorators 10/10. **Contract pins** (green on every revert): the two "stored entity still writes/updates" tests per
  sync/async class.
- **Live runs, all on throwaway local containers removed afterwards:** PostgreSQL 127/127, MySQL 149/149, SQL Server
  163/163 (via `127.0.0.1`: SqlClient's `localhost` went to IPv6 against an IPv4-only bind), TimescaleDB 68/68, MongoDB
  118/118 (replica set), RavenDB 81/81, Elasticsearch 198/198 (new gate `BIRKO_ES_URL`, no CI job yet), CosmosDB 75/75.
- **Inverted / adjusted, not deleted (rule 56):** 5 in-memory tests (silent miss / no-resurrect now also assert the
  throw), 1 JSON (CR-L129 ghost), and 4 fixtures (3 sluggable bulk, 1 tenant) that updated never-stored entities while
  testing enumeration — they now store them first.
- **Full solution run (no live servers):** 169 test projects, 168 green on the first run; the one red (`ViewModelUpdateMergeTests.Updating_a_view_model_whose_row_does_not_exist_behaves_as_before`) asserted NotThrow because the store miss used to be silent — inverted to `…_reports_the_stores_miss` (the merge still adds no failure of its own); ViewModel 37/37.
- **Not done here:** a replace inside a CosmosDB `TransactionalBatch` is still reported only at the batch's atomic commit.
  `BIRKO_ES_URL` has no `live-tests.yml` job.
- **Merge gate:** inline review. Public surface: new `EntityNotFoundException`, `StoreSave`; SQL connector `Update`
  overloads return `int` and `BulkUpdate(Async)` return the unmatched models (source-compatible). No nullable warnings.

## Progress log

- step 2 — picked; ranked above TASK-556 (bulk create overwrites a caller Guid) on key 1/3: Update/Save report success while writing nothing on every backend (silent data loss on the default path), while TASK-556 stores different ids (wrong, but the rows exist); decisions recorded 2026-10-11 (4e6ef5f2)
- step 3 — verified: held as written (in-memory `ContainsKey` guard, SQL 0-row UPDATE, Mongo MatchedCount unread, base `Save` returns the Guid after a no-op Update); wider: ~30 Save bodies re-implement the routing
- step 4 — layer: local, cross-provider; implementation plan written (units A-F)
- step 5 (in progress) — unit A done (EntityNotFoundException, StoreSave, base Save/SaveAsync); InMemory sync+async throw on single and bulk miss, 5 silent-miss tests inverted; shared contract `tests/Shared/StoreContract/UpdateMissContract.cs` (6 sync + 6 async) linked via `link_contract` into InMemory.Tests — 108/108. Next: JSON, XML
- step 5 (in progress) — JSON (base sync+async) and XML (base, batch, separate × sync+async) throw on single and bulk miss; found and fixed: `AbstractAsyncXmlStore` bulk update had NO existence guard (silently created entities); separate-store bulk collects per-item misses. Contract linked: JSON 49/49 (2 stores), XML 68/68 (6 layouts); 1 JSON test inverted (CR-L129 ghost). Next: SQL
- step 5 (in progress) — SQL: connector `Update`/`UpdateAsync` (object/Type/Table/string overloads) return rows matched (-1 = not sent); `DataBaseStore`/`AsyncDataBaseStore` throw on 0; generic bulk collects per-item misses; `BulkUpdate(Async)` on SQLite/MSSql/MySQL/PostgreSQL return unmatched models and the 10 provider stores (incl. TimescaleDB) throw via `EntityNotFoundException.ThrowIfAny`. FOUND (wider, same root): those connectors' BulkUpdate AND BulkDelete (×4 each) took the key from `table.GetPrimaryFields()` (no Guid fallback) — for a model with no [PrimaryField] bulk update wrote nothing and bulk delete deleted nothing, silently; measured on SQLite (0 of 2 deleted, rename lost). All 16 now use `DataBase.GetPrimaryFields(type)`; `BulkPrimaryKeyFallbackTests` (3). SQL 710/710, SQLite 472/472 (contract 12/12). Remaining: unit B (wrapper Saves), Mongo, Raven, Cosmos, ES, Influx, live PG/MySQL/MSSql, step 6 split, docs, close
- step 5 (in progress) — unit B done: 20 decorator Save/SaveAsync bodies (EventSourcing ×2, Localization ×4, Versioned ×2, Audit ×2, Default ×2, Sluggable ×2, SoftDelete ×2, Timestamp ×2, Tenant ×2) route through StoreSave with their own create/update; Telemetry, Validation, both repositories and MirrorDataSource already forwarded to the inner Save; workflow instance stores have their own read-then-write API (unaffected). `DecoratorSaveUpsertTests` (10) — Patterns 85/85. 4 fixtures (3 sluggable bulk, 1 tenant) stored their entities first: they tested enumeration/concurrency and had silently relied on updating unstored entities. Suites green: EventSourcing, Localization, Repositories, Tenant 80, Telemetry, Validation, Localization.Data, Sync.Tenant, Rules, Data.Core. Next: MongoDB, RavenDB, Cosmos, ES, InfluxDB (+ native SaveAsync overrides on Mongo/Cosmos/Influx)
- step 5 (in progress) — MongoDB sync+async: ReplaceOne's MatchedCount (acknowledged writes only) → throw; bulk collects; contract gained a `Ready()` hook so live providers report SKIPPED (fail under BIRKO_REQUIRE_LIVE). FOUND: `AsyncMongoDBStore.SaveAsync`'s native-upsert branch never invoked the store delegate — fixed. Live (mongo:7 rs0, throwaway): 118/118; Mongo stores reverted → 6/12 contract red (incl. the delegate-once test). Next: RavenDB, Cosmos, ES, InfluxDB; then live PG/MySQL/MSSql
- step 5 (in progress) — RavenDB sync+async: FOUND Update stored unconditionally (an update of a missing entity silently CREATED it); the load it always did now throws on null, bulk collects. Live (ravendb:latest, throwaway) 81/81; reverted → 4/12 red. ElasticSearch sync+async: `document_missing_exception` (single 404, bulk item errors) → EntityNotFoundException via `ElasticSearch.IsDocumentMissing` / `ThrowIfOnlyMissingDocuments`; other errors keep the generic failure. Contract gained `Settle()` (ES refreshes — near-real-time search). New gate `BIRKO_ES_URL` (no CI job). Live (7.17.25, throwaway) 198/198; reverted → 8/12 red. Next: CosmosDB, InfluxDB, live PG/MySQL/MSSql/Timescale
- step 5 (in progress) — CosmosDB sync+async: item-level 404 on replace (sub-status != 1003, a missing container still propagates) → EntityNotFoundException; bulk replaced Task.WhenAll's first-failure-only with per-item miss collection; a replace inside a TransactionalBatch is left to the batch's atomic commit. FOUND: `AsyncCosmosDBStore.SaveAsync` overrode the public wrapper without the lazy-init gate — as the first call on a store it returned Guid.Empty and wrote nothing; fixed (`EnsureInitializedAsync`). Live (vnext-preview emulator, Gateway, throwaway) 75/75; reverted → 8/12 red. InfluxDB: DECIDED to document Update as an upsert (a point write has no matched count; detecting the miss needs a query per write, which the decision rules out; the write is applied, not lost) — remarks on both UpdateCore; contract NOT linked for Influx. Next: live PG/MySQL/MSSql/Timescale contract
- step 5 — complete: every unit A–E done (see entries above); Influx documented, not changed
- step 6 — per-provider reverts all red (numbers in Outcome); decorators 10/10; base Save 4/12
- step 7 — respecced store-crud-contract: new requirement *An update of an entity that is not stored throws* (3 scenarios incl. the SQL key fallback); *Save routes…* extended with the upsert and its 3 scenarios; the in-memory requirement retitled and its 3 scenarios inverted; bulk partial-update scenario inverted
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
