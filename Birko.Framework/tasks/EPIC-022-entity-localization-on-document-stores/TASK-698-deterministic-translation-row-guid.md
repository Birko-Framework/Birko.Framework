---
id: TASK-698
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-20
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A translation row's Guid derived from its key, so a race is retried on every provider

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). The upsert in `LocalizedTranslationRows` retries a refused create as an
update, but only a unique key refuses it, and only SQL holds one (TASK-680). Every store already refuses a second create
with the **same Guid** as `StoreUniqueViolationException` (TASK-625). If a translation row's Guid were a name-based
UUID (v5) of `(EntityGuid, EntityType, FieldName, Culture)`, every provider would refuse a racing duplicate the same way,
with no unique index: ElasticSearch, RavenDB, Cosmos DB, JSON, XML and InMemory included.

## Acceptance criteria

- [x] Measure first: a racing create of the same key on each non-SQL provider stores two rows today (rule 54).
- [x] The Guid is derived in one producer from the four-part key (the culture's name normalised as stored), and every
      create path uses it.
- [x] Decide what happens to rows already stored with random Guids: read by key as today and rewritten on next save,
      left alone, or migrated. Write the decision down.
- [x] Live or in-memory tests per provider: the race leaves one row, holding the last writer's value.
- [x] The consumer-supplied translation models (TASK-680) keep working; a model whose Guid the caller sets is refused or
      honoured, and the choice is decided and written down.

## Out of scope

- Indexes (TASK-699), transactions (TASK-700).

## Human test plan

N/A — asserted by the suites.

## Implementation plan

_Planned 2026-10-20. Decided by the user:_
- _rows already stored with random Guids are left alone. They are found and updated by key, never by Guid, so mixed
  state is harmless;_
- _a soft-delete-wrapped translation store is refused, on every provider. SQL already fails there: its unique key
  counts a soft-deleted row, and the retry cannot see it (read, to be measured)._

**Premise measured (2026-10-20, live where gated).** The shared store contract's
`*_of_a_stored_guid_is_refused_and_the_stored_entity_is_kept` (single, bulk and async) passes on all seven: InMemory,
JSON, XML, MongoDB, ElasticSearch, RavenDB and Cosmos DB. Each refuses a second create of a stored Guid with the typed
`StoreUniqueViolationException`. TimescaleDB accepts duplicates (TASK-567), but its translations sit behind the SQL
unique key.

1. **One producer:** `LocalizedTranslationRows.KeyGuid(entityGuid, entityType, fieldName, culture)`, an RFC 4122
   name-based UUID (v5, SHA-1, a fixed namespace) of the four values exactly as stored. Every row the upsert creates
   gets it, so two writers' creates of one key collide on the Guid. The retry-as-update then takes over unchanged.
2. **Move `ISoftDeleteStoreWrapper` down** from Birko.Data.Patterns to Birko.Data.Stores. It is an empty marker, and no
   consumer uses it, so Localization can name it without a new dependency. The localized wrappers and
   `EntityTranslations` refuse a translation store whose chain (`StoreChain.Walk`) holds one, next to
   `RequireQueryableModel`.
3. **Tests:**
   - `KeyGuid`: deterministic, distinct per component, version and variant bits, a vector cross-checked against
     Python's `uuid.uuid5`.
   - Measure first: a race on InMemory stores two rows today (rule 54).
   - A shared race test linked into the InMemory, JSON, XML, MongoDB, ElasticSearch, RavenDB and Cosmos DB test projects
     (live where gated): the race leaves one row with a writer's value, stored under `KeyGuid`. A key deleted and saved
     again works.
   - The soft-delete refusal, plus the SQL latent case measured on SQLite.
   - Mutations. Regression: every importer of Birko.Data.Localization and Birko.Data.Patterns.

## Outcome

Done 2026-10-20.

**Measured before:** a race on InMemory stored two rows for one key, each under a random Guid.

**Production change:**
- `LocalizedTranslationRows.KeyGuid` (RFC 4122 v5, cross-checked with Python's `uuid.uuid5`) stamps every row the upsert
  creates.
- `IRealtimeReadStore` / `IAsyncRealtimeReadStore` (new, Birko.Data.Stores) let the post-refusal re-read ask by Guid.
  ElasticSearch implements them with a realtime GET (a sync `ReadRealtime` was added), RavenDB with its ACID load by id.
  Only the store handed in is asked, never one beneath a decorator.
- `ISoftDeleteStoreWrapper` moved to Birko.Data.Stores. The four wrappers and `EntityTranslations` refuse a
  soft-delete-wrapped translation store.

**Measured after, live where gated:** InMemory, JSON, XML, MongoDB, ElasticSearch, RavenDB and Cosmos DB, through the
shared `tests/Shared/LocalizationContract/TranslationRaceContract.cs`:
- a race leaves one row with the retrying writer's value;
- a row is stored under its `KeyGuid`;
- a key removed and saved again is stored once;
- a key held outside the store's scope is refused with the message, and the hidden row is untouched.

ElasticSearch first failed the race: its search missed the other writer's row inside the refresh window. The realtime
re-read fixed that, as decided by the user. On SQL (SQLite, PostgreSQL, SQL Server, MySQL) a soft-deleted translation
still holds its key, so saving it again is refused, which confirms the reason for the refusal.

**Close review, handled:**
- **Medium, decided by the user: scopes must match.** A tenant-scoped translation store over entities the tenants share
  collided on the key Guid, and on ElasticSearch and RavenDB the realtime read reached beneath the tenant decorator.
  Now the realtime read is asked only of the store handed in (a non-scoping decorator forwards it, as the test racer
  does). A refused key with no visible holder throws `StoreUniqueViolationException` saying a translation store scoped
  narrower than its entities holds one row per key. The README says to translate shared entities through an unscoped
  store. SQL's unique key already refused this case.
- **Low, fixed:**
  - the "exactly as stored" wording;
  - the displaced XML docs on the ElasticSearch and RavenDB members;
  - RavenDB named as an implementer;
  - the namespace change named in the breaking note.
- **Recorded:**
  - The race contract is async-only. The sync ElasticSearch `ReadRealtime` has its own live test.
  - The RavenDB contract settles with a 1.5 s delay, as the other RavenDB live tests do.
  - Cosmos relies on the emulator's session consistency.

**Tests:**
- `TranslationKeyGuidTests` (8);
- the race contract, 4 per provider, on 7 providers;
- `ElasticSyncRealtimeReadTests`;
- SQL `TASK698_a_soft_deleted_translation_still_holds_its_key…` on 4 providers.

**Mutations, all red (8):**
- key Guid not stamped (2 red each on InMemory, MongoDB and ElasticSearch);
- separator dropped;
- version bits not set;
- realtime re-read removed (ElasticSearch);
- soft delete looked for on the top store only;
- one wrapper skipping the refusal;
- realtime read beneath decorators (ElasticSearch and RavenDB);
- the explaining refusal dropped.

**Regression, live where gated:**
- Localization 302/302, Localization.SQL 124/124;
- Patterns 161, InMemory 141, JSON 73, XML 134, MongoDB 153, ElasticSearch 298, RavenDB 123, Cosmos DB 132;
- BackgroundJobs.ElasticSearch 22, Data 240, Composition 26.

The solution and Sandbox build with no new warnings. The `AsyncElasticSearchStore.cs:194` CS8629 predates this change.
