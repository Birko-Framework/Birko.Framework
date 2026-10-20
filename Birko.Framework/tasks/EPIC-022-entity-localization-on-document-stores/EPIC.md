---
id: EPIC-022
# status — one of: planned, in-progress, done, cancelled
status: in-progress
created: 2026-10-20
owner: ai
affects: [Birko.Data.Localization, Birko.Data.MongoDB, Birko.Data.RavenDB, Birko.Data.CosmosDB, Birko.Data.ElasticSearch]
---

# Entity localization on the non-SQL providers

## Area of concern

[[EPIC-021]] made `Birko.Data.Localization` fit real catalogues, but several of its guarantees landed only on SQL:
- the unique four-part key, which turns a race into a retried update (TASK-680/684);
- the entity-and-translations transaction (TASK-683);
- the `ValueHash` index (TASK-681);
- PostgreSQL's in-place retry (TASK-694).

The generic parts work on every provider: batched reads and writes (TASK-679/690), the base-value fallback, and
chunking (TASK-686).

Read from source on 2026-10-20, not measured:
- On JSON, XML, InMemory, RavenDB, Cosmos DB and ElasticSearch nothing refuses a second translation row for the same
  key, so two writers can create duplicates and the upsert's retry never fires. RavenDB, Cosmos DB and ElasticSearch
  index managers refuse a unique index outright.
- MongoDB can hold a unique index and already translates a duplicate key into `StoreUniqueViolationException`, but
  nothing declares the index.
- MongoDB, RavenDB and Cosmos DB have units of work, but no localized write boundary uses them.
- The 500-GUID chunk budget was sized to SQL Server's parameter limit. It is unmeasured against ElasticSearch `terms`,
  Cosmos `IN` and RavenDB query limits.

Filed at the user's request, 2026-10-20 (all four follow-ups chosen).

## Stories / tasks

- [[TASK-698]] Deterministic translation row Guid, so the race retry works on every provider
- [[TASK-699]] MongoDB: declare the translation key and ValueHash indexes
- [[TASK-700]] A localized write boundary for MongoDB, RavenDB and Cosmos DB
- [[TASK-701]] Measure the chunk budget on the non-SQL providers

## Out of scope

- The SQL providers (EPIC-021, TASK-694).
