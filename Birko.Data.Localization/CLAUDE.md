# Birko.Data.Localization

## Overview
Entity-level localization for Birko.Data stores. Provides transparent translation of entity fields via store decorator wrappers.

## Project Location
`C:\Source\Birko.Data.Localization\` (shared project, .shproj)

## Components

### Models (`Models/`)
- **ILocalizable** — Interface for entities with translatable fields. Returns `IReadOnlyList<string>` of property names.
- **EntityTranslationModel** — Persisted translation entity: `EntityGuid`, `EntityType`, `FieldName`, `Culture`, `Value`, `UpdatedAt`. Extends `AbstractModel`.
- **IEntityLocalizationContext** — Provides `CurrentCulture` and `DefaultCulture` for determining which language to read/write.

### Filters (`Filters/`)
- **EntityTranslationFilter** — Query builder with static factories: `ByEntity`, `ByEntityAndCulture`, `ByEntityFieldAndCulture`, `ByEntityType`, `ByEntityTypeAndCulture`.

### Decorators (`Decorators/`)
- **LocalizedStoreWrapper** — Sync `IStore<T>` decorator
- **AsyncLocalizedStoreWrapper** — Async `IAsyncStore<T>` decorator
- **LocalizedBulkStoreWrapper** — Sync `IBulkStore<T>` decorator
- **AsyncLocalizedBulkStoreWrapper** — Async `IAsyncBulkStore<T>` decorator

All wrappers:
- Intercept reads to apply translations (when current culture != default culture)
- Intercept creates/updates to persist translations (upsert per field)
- Intercept deletes to remove all associated translations
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
