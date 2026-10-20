# Birko.Data.Localization

## Overview
Entity-level localization for Birko.Data stores. Provides transparent translation of entity fields via store decorator wrappers.

## Project Location
`Birko.Data.Localization/` (shared project, .shproj)

## Components

### Models (`Models/`)
- **ILocalizable** — Interface for entities with translatable fields. Returns `IReadOnlyList<string>` of property names.
- **IEntityTranslation** — The translation row contract the wrappers are generic over (`TTranslation : AbstractModel, IEntityTranslation`, TASK-680); a consumer's own model (e.g. a tenant entity) implements it, with the members as public properties (checked at construction). Rows are created through the store's `CreateInstance()`.
- **EntityTranslationModel** — The default `IEntityTranslation`, persisted translation entity: `EntityGuid`, `EntityType`, `FieldName`, `Culture`, `Value`, `UpdatedAt`. Extends `AbstractModel`.
- **IEntityLocalizationContext** — Provides `CurrentCulture` and `DefaultCulture` for determining which language to read/write.

### Filters (`Filters/`)
- **EntityTranslationFilter** — Query builder with static factories: `ByEntity`, `ByEntityAndCulture`, `ByEntityFieldAndCulture`, `ByEntityType`, `ByEntityTypeAndCulture`.

### Decorators (`Decorators/`)
- **LocalizedEntityFields** — the one producer for capturing/restoring localizable values and detaching an entity
- **KeyGuid / RequireNoSoftDelete (TASK-698)** — `LocalizedTranslationRows.KeyGuid(entityGuid, entityType, fieldName, culture)` is the one producer of a new translation row's Guid: an RFC 4122 v5 UUID (SHA-1, namespace `KeyGuidNamespace`, never change it) of the four values the save passes (which are the values stored), joined by U+001F (written as the `\u001f` escape, never a raw control character). Two writers of one key collide on the Guid, so the upsert's retry-as-update runs on every provider (measured live on InMemory, JSON, XML, MongoDB, ElasticSearch, RavenDB and Cosmos DB through the shared `tests/Shared/LocalizationContract/TranslationRaceContract.cs`). After a refusal the re-read also asks by Guid through `IRealtimeReadStore` / `IAsyncRealtimeReadStore` when the translation store handed in offers it (ElasticSearch, RavenDB); never beneath a decorator, which may scope reads. A refused key no visible row holds throws `StoreUniqueViolationException` naming the likely cause: a translation store scoped narrower than its entities (tenant translations of shared entities) holds one row per key, so translate shared entities through an unscoped store (decided by the user). Old random-Guid rows are left alone (decided by the user). The four wrappers and `EntityTranslations` refuse a translation store chain holding an `ISoftDeleteStoreWrapper` (decided by the user; measured on SQL: the hidden row keeps the key)
- **LocalizedFilterResolution** — the one producer of a filter rewritten for a non-default culture (TASK-681): per condition `guid ∈ T ∨ (Part ∧ guid ∉ H)` — translated matches (by `ValueHash` for ==/IN), or the base value for entities without a translation. Above `ReadBatchSize` GUIDs across every T and H (TASK-686) it rewrites the filter to one positive set S = ∩ (T ∪ (C \ H)), using the candidates C it has already read, because a NOT IN cannot be split, and returns disjoint chunked queries. An empty S means no query. An entity store that cannot list is refused (decided by the user)
- **LocalizedQueries / LocalizedChunkedReads** — the rewritten filter as one query, N disjoint chunks or none, and the one place that runs it: read all, read first, count (sum), read up to a limit (Find/Exists), order-and-page in memory (decided by the user). Every wrapper path and `EntityTranslations` go through it
- **LocalizedTranslationRows** — the one producer for reading, writing and deleting translation rows (TASK-168, TASK-194, TASK-679, TASK-680): the four-part key `(EntityGuid, EntityType, FieldName, Culture)`, filters built with `Expression.Property` on the concrete translation type (a precaution, not a measured failure), keyed by the GUID the store returned, `EntityType` from `EntityTypeOf<T>()`, `null` removes the row, list reads batched per 500 GUIDs, and writes batched (TASK-690): `SaveMany` is one read of the existing rows per 500 entities, then at most one bulk create, update and delete, creates first; a create the unique key refuses re-reads the keys it was creating once and updates the rows another writer holds, and a second refusal rethrows the original; `DeleteMany` / `DeleteCulture` are chunked filter deletes and an empty list makes no call; `LocalizeAll` is the one localize path; `ReadAllCultures` reads one entity's every culture in one read
- **LocalizedStoreWrapper** — Sync `IStore<T>` decorator
- **AsyncLocalizedStoreWrapper** — Async `IAsyncStore<T>` decorator
- **LocalizedBulkStoreWrapper** — Sync `IBulkStore<T>` decorator
- **AsyncLocalizedBulkStoreWrapper** — Async `IAsyncBulkStore<T>` decorator

Each wrapper is `X<TStore, T, TTranslation>`; `X<TStore, T>` is the same wrapper with `EntityTranslationModel`.

All wrappers:
- Intercept reads to apply translations (when current culture != default culture)
- Intercept creates/updates to persist translations, one batched write per call however many entities (`LocalizedTranslationRows.SaveMany`); a filter-based update reads the matched entities and their translations once, writes each entity through a detached copy (sync and async alike), and when it fails part way still saves, best effort, the translations of the entities already written (decided by the user), the original error always surfacing
- Refuse a filter that reads a localizable field in a form the analyzer cannot translate (`NotSupportedException`), never test it against the base column
- Validate the declared localizable fields once, in the constructor (`LocalizedEntityFields.LocalizableFieldsOf`)
- Run every write that touches both stores through the boundary: non-default-culture creates and updates (the filter update included), and every delete. A collection is materialized first, because a retried boundary re-runs the write. Inside an atomic boundary the filter update skips its best-effort save. A collection update that reports items not stored (`EntityNotFoundException`) saves the stored items' translations, then rethrows
- Intercept deletes to remove all associated translations, as one filter delete per 500 entities; `Destroy` deletes this type's rows, `Init` initialises the translation store
- Implement `IStoreWrapper<T>` for decorator introspection
- Use reflection to get/set localizable string properties

### Boundaries (`Boundaries/`)
- **ILocalizedWriteBoundary** — how a write touching both stores is one unit (TASK-683); `IsAtomic`, `Run`, `RunAsync`. The SQL implementation lives in Birko.Data.Localization.SQL, so this project stays storage-free
- **BestEffortWriteBoundary** — the default: runs the write as it is
- **LocalizedCleanup** — the one producer of a delete's translation cleanup: entity first, then rows; on a non-atomic boundary a failure becomes **TranslationCleanupException** (`RemainingEntityGuids`, the original as inner; rule 22), on an atomic one it propagates and rolls back

### Translations (`Translations/`)
- **IEntityTranslations<T>** / **EntityTranslations<T, TTranslation>** (`EntityTranslations<T>` = with `EntityTranslationModel`) — the explicit API (TASK-682): `GetAsync`, `GetAllCulturesAsync`, `SetAsync`, `RemoveCultureAsync`, `RemoveEntitiesAsync`, `FindEntityAsync`, `ExistsAsync`, `LocalizeAsync`. Each is a thin call into `LocalizedTranslationRows` or `LocalizedFilterResolution`; `Find`/`Exists` share one producer (`x.F == v && x.Guid != except`, rewritten as a wrapper's read, read with a limit). Built over the unlocalized entity store (a localized wrapper anywhere in the `IStoreWrapper` chain is refused), the translation store and the context (`DefaultCulture` only). Default culture: `SetAsync` refuses it, lookups test the base column, `LocalizeAsync` returns the entities unchanged. An undeclared field is refused (rule 41), an ambiguous `FindEntityAsync` throws naming two GUIDs (rule 26)
- **EntityTranslationsServiceCollectionExtensions** — `AddEntityTranslations<T>()`, `<T, TTranslation>()`, and a factory overload for when the container's entity store is the localized wrapper

## Dependencies
- **Birko.Data.Core** — `AbstractModel`
- **Birko.Data.Stores** — `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`, `IStoreWrapper`, `StoreDataDelegate`
- **Microsoft.Extensions.DependencyInjection.Abstractions** — only `EntityTranslationsServiceCollectionExtensions`; consumer-supplied, not declared in the `.projitems` (Birko.Data.Repositories declares it, a second declaration collides, NU1504)

## Key Patterns
- Follows the same decorator pattern as `Birko.Data.Patterns` (Audit, Timestamp, SoftDelete wrappers)
- Translation store is injected separately — any Birko.Data store backend can hold the translations
- Culture context is injectable — supports per-request culture resolution (HTTP headers, user preferences, etc.)

## Maintenance
- When adding new store interface methods to `Birko.Data.Stores`, add corresponding implementations to all four wrapper classes
- When changing `EntityTranslationModel`, update `CopyTo` and `EntityTranslationFilter`
