# Birko.Localization.Data

Database-backed translation provider for the Birko.Localization framework. Stores translations in any Birko.Data store backend.

## Features

- **Store-agnostic** — Works with any `IAsyncBulkReadStore<TranslationModel>` (SQL, MongoDB, ElasticSearch, JSON, RavenDB, etc.)
- **Built-in caching** — In-memory TTL cache (default 5 min, configurable, or disable with `TimeSpan.Zero`)
- **Namespace scoping** — Isolate translations per module/feature
- **Async support** — `GetTranslationAsync` and `GetAllAsync` for non-blocking access
- **Cache invalidation** — Manual `InvalidateCache()` after write operations

## Usage

```csharp
// Create a store (any Birko.Data store works)
var store = new AsyncDataBaseBulkStore<MsSqlConnector, TranslationModel>();
store.SetSettings(dbSettings);
await store.InitAsync();

// Create the provider
var provider = new DatabaseTranslationProvider(store);

// Use with Localizer
var settings = LocalizationSettings.Default
    .WithDefaultCulture(CultureInfo.GetCultureInfo("en"));
var localizer = new Localizer(provider, settings);

var greeting = localizer.Get("greeting", CultureInfo.GetCultureInfo("sk")); // "Ahoj"
```

### Namespace Scoping

```csharp
var ordersProvider = new DatabaseTranslationProvider(store, @namespace: "orders");
var authProvider = new DatabaseTranslationProvider(store, @namespace: "auth");
```

### Composite with Fallback

```csharp
// Database overrides with JSON file fallback
var composite = new CompositeTranslationProvider(
    new DatabaseTranslationProvider(store),
    new JsonTranslationProvider("/locales")
);
```

### Cache Configuration

```csharp
// Custom TTL
var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(30));

// No caching
var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.Zero);

// Invalidate after writes
provider.InvalidateCache("sk");     // specific culture
provider.InvalidateCache();          // all cultures
```

## TranslationModel

| Property | Type | Description |
|----------|------|-------------|
| Guid | Guid? | Unique identifier (inherited) |
| Key | string | Translation key (e.g., "greeting") |
| Culture | string | Culture name (e.g., "sk", "en-US") |
| Value | string | Translated text |
| Namespace | string? | Optional scope (e.g., "orders") |
| UpdatedAt | DateTime? | Last modification timestamp |

## License

MIT License - see [License.md](License.md) for details.
