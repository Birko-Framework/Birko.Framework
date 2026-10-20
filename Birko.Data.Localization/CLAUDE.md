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
- **LocalizedFilterResolution** — the one producer of a filter rewritten for a non-default culture (TASK-681): per condition `guid ∈ T ∨ (Part ∧ guid ∉ H)` — translated matches (by `ValueHash` for ==/IN), or the base value for entities without a translation
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
