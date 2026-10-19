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
- **LocalizedTranslationRows** — the one producer for reading, writing and deleting translation rows (TASK-168, TASK-194, TASK-679, TASK-680): the four-part key `(EntityGuid, EntityType, FieldName, Culture)`, filters built with `Expression.Property` on the concrete translation type (a precaution, not a measured failure), keyed by the GUID the store returned, `EntityType` from `EntityTypeOf<T>()`, `null` removes the row, a create refused by the unique key retried as an update, list reads batched per 500 GUIDs
- **LocalizedStoreWrapper** — Sync `IStore<T>` decorator
- **AsyncLocalizedStoreWrapper** — Async `IAsyncStore<T>` decorator
- **LocalizedBulkStoreWrapper** — Sync `IBulkStore<T>` decorator
- **AsyncLocalizedBulkStoreWrapper** — Async `IAsyncBulkStore<T>` decorator

Each wrapper is `X<TStore, T, TTranslation>`; `X<TStore, T>` is the same wrapper with `EntityTranslationModel`.

All wrappers:
- Intercept reads to apply translations (when current culture != default culture)
- Intercept creates/updates to persist translations (upsert per field, through `LocalizedTranslationRows`)
- Refuse a filter that reads a localizable field in a form the analyzer cannot translate (`NotSupportedException`), never test it against the base column
- Validate the declared localizable fields once, in the constructor (`LocalizedEntityFields.LocalizableFieldsOf`)
- Intercept deletes to remove all associated translations; `Destroy` deletes this type's rows, `Init` initialises the translation store
- Implement `IStoreWrapper<T>` for decorator introspection
- Use reflection to get/set localizable string properties

## Dependencies
- **Birko.Data.Core** — `AbstractModel`
- **Birko.Data.Stores** — `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`, `IStoreWrapper`, `StoreDataDelegate`

## Key Patterns
- Follows the same decorator pattern as `Birko.Data.Patterns` (Audit, Timestamp, SoftDelete wrappers)
- Translation store is injected separately — any Birko.Data store backend can hold the translations
- Culture context is injectable — supports per-request culture resolution (HTTP headers, user preferences, etc.)

## Maintenance
- When adding new store interface methods to `Birko.Data.Stores`, add corresponding implementations to all four wrapper classes
- When changing `EntityTranslationModel`, update `CopyTo` and `EntityTranslationFilter`
