# Birko.Localization.Data

## Overview
Database-backed translation provider for Birko.Localization. Works with any Birko.Data async bulk store (SQL, MongoDB, ElasticSearch, JSON, RavenDB, etc.).

## Project Location
- **Path:** `Birko.Localization.Data/`
- **Type:** Shared Project (.shproj/.projitems)
- **Namespace:** `Birko.Localization.Data`

## Components

### Models (`Models/`)
- **TranslationModel** — Extends `AbstractModel`. Fields: Key, Culture, Value, Namespace (optional scoping), UpdatedAt

### Filters (`Filters/`)
- **TranslationFilter** — Query builder with static factories: ByCulture, ByKeyAndCulture, ByNamespaceAndCulture

### Provider
- **DatabaseTranslationProvider** — Implements `ITranslationProvider` from Birko.Localization. Accepts any `IAsyncBulkReadStore<TranslationModel>`. Built-in TTL cache (default 5 min, configurable). Supports namespace scoping. Provides async methods (GetTranslationAsync, GetAllAsync) alongside sync ITranslationProvider contract. Manual cache invalidation via InvalidateCache().

## Dependencies
- **Birko.Localization** — ITranslationProvider interface
- **Birko.Data.Core** — AbstractModel
- **Birko.Data.Stores** — IAsyncBulkReadStore<T>

## Usage Pattern
```csharp
// With any store (SQL example)
var store = new AsyncDataBaseBulkStore<MsSqlConnector, TranslationModel>();
store.SetSettings(dbSettings);
await store.InitAsync();

var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(10));
var localizer = new Localizer(provider, settings);

// With namespace scoping
var ordersProvider = new DatabaseTranslationProvider(store, @namespace: "orders");
```

## Maintenance
- TranslationModel can be extended with SQL attributes ([Table], [NamedField], [PrimaryField]) in consuming projects
- Cache invalidation should be called after CRUD operations on translations
