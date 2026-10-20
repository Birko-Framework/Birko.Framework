# Birko.Localization

## Overview

Birko.Localization provides a pluggable localization system with culture fallback chains, CLDR-based pluralization, and multiple translation storage backends. It is a shared project (.shproj) with no external dependencies.

## Architecture

```
ILocalizer (main API)
    └── Localizer (default implementation)
            ├── ITranslationProvider (backend)
            │   ├── JsonTranslationProvider
            │   ├── ResxTranslationProvider
            │   ├── InMemoryTranslationProvider
            │   └── CompositeTranslationProvider
            ├── ICultureResolver
            │   └── ThreadCultureResolver
            └── LocalizationSettings
                └── MissingKeyBehavior
```

## Culture Fallback Chain

When resolving a translation key, the `Localizer` follows this fallback chain:

1. **Exact culture** — e.g., `sk-SK`
2. **Parent culture** — e.g., `sk` (if `FallbackToParentCulture` is enabled)
3. **Default culture** — configured in `LocalizationSettings`
4. **Missing key behavior** — `ReturnKey`, `ReturnEmpty`, or `ThrowException`

## Translation Providers

### JsonTranslationProvider

Loads translations from JSON files named `{culture}.json`:

```
locales/
├── en.json
├── sk.json
└── sk-SK.json
```

Supports flat and nested key formats:

```json
{
  "greeting": "Hello",
  "errors": {
    "notFound": "Not found"
  }
}
```

Nested keys are automatically flattened: `errors.notFound`.

### ResxTranslationProvider

Loads from standard .resx XML files:

```
resources/
├── Messages.resx        (default culture)
├── Messages.en.resx
└── Messages.sk.resx
```

### InMemoryTranslationProvider

Dictionary-based provider with a fluent builder:

```csharp
var provider = InMemoryTranslationProvider.Create()
    .AddTranslation("en", "greeting", "Hello")
    .AddTranslation("sk", "greeting", "Ahoj")
    .Build();
```

### CompositeTranslationProvider

Chains multiple providers by priority (first non-null result wins):

```csharp
var composite = new CompositeTranslationProvider(
    databaseOverrides,  // highest priority
    jsonDefaults        // fallback
);
```

## CLDR Pluralization

The `CldrPluralizer` implements CLDR plural rules for 30+ languages:

| Language Family | Forms | Languages |
|----------------|-------|-----------|
| East Asian | 1 (other) | zh, ja, ko, vi, th |
| Germanic/Romance | 2 (one, other) | en, de, nl, sv, es, it, pt |
| French | 2 (one incl. 0, other) | fr |
| Czech/Slovak | 3 (one, few 2-4, other) | sk, cs |
| Polish | 3 (one, few, other) | pl |
| East Slavic | 3 (one, few, other) | ru, uk, hr, sr |
| Arabic | 6 (zero, one, two, few, many, other) | ar |

### Example: Slovak

```csharp
var pluralizer = new CldrPluralizer();
var sk = CultureInfo.GetCultureInfo("sk");

pluralizer.GetPluralForm(1, sk);  // 0 → "1 deň"
pluralizer.GetPluralForm(3, sk);  // 1 → "3 dni"
pluralizer.GetPluralForm(5, sk);  // 2 → "5 dní"
```

## String Interpolation

### Named placeholders

```csharp
localizer.Get("welcome",
    new Dictionary<string, object?> { ["userName"] = "John" },
    culture);
// Template: "Welcome, {userName}!" → "Welcome, John!"
```

### Positional placeholders

```csharp
localizer.Get("items", new object[] { 5 }, culture);
// Template: "You have {0} items" → "You have 5 items"
```

## Formatting

### NumberFormatter

```csharp
var formatter = new NumberFormatter();
formatter.Format(1234.56m, enUS);           // "1,234.56"
formatter.FormatCurrency(99.99m, "USD", enUS); // "$99.99"
formatter.FormatPercent(0.75m, enUS);       // "75.00 %"
```

### DateFormatter

```csharp
var formatter = new DateFormatter();
formatter.Format(date, enUS);                    // "3/18/2026"
formatter.Format(date, "yyyy-MM-dd", invariant); // "2026-03-18"
formatter.FormatRelative(past, now);             // "5 minutes ago"
formatter.FormatRelative(future, now);           // "in 3 hours"
```

## Database Translation Provider (Birko.Localization.Data)

`DatabaseTranslationProvider` works with any `IAsyncBulkReadStore<TranslationModel>`:

```csharp
// With any Birko.Data store (SQL, MongoDB, ElasticSearch, JSON, etc.)
var store = new AsyncDataBaseBulkStore<MsSqlConnector, TranslationModel>();
store.SetSettings(dbSettings);
await store.InitAsync();

var provider = new DatabaseTranslationProvider(store);
var localizer = new Localizer(provider, settings);
```

### Namespace Scoping

Isolate translations per module:

```csharp
var ordersProvider = new DatabaseTranslationProvider(store, @namespace: "orders");
var authProvider = new DatabaseTranslationProvider(store, @namespace: "auth");
```

### Caching

Built-in TTL cache (default 5 min):

```csharp
// Custom TTL
var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.FromMinutes(30));

// Disable caching
var provider = new DatabaseTranslationProvider(store, cacheDuration: TimeSpan.Zero);

// Invalidate after writes
provider.InvalidateCache("sk");  // single culture
provider.InvalidateCache();       // all cultures
```

### Composite: Database + JSON Fallback

```csharp
var composite = new CompositeTranslationProvider(
    new DatabaseTranslationProvider(store),  // runtime overrides
    new JsonTranslationProvider("/locales")  // default translations
);
```

### TranslationModel

| Property | Type | Description |
|----------|------|-------------|
| Guid | Guid? | Unique identifier (inherited from AbstractModel) |
| Key | string | Translation key |
| Culture | string | Culture name (e.g., "sk", "en-US") |
| Value | string | Translated text |
| Namespace | string? | Optional scope |
| UpdatedAt | DateTime? | Last modification |

## Custom Translation Provider

Implement `ITranslationProvider` for custom backends:

```csharp
public class MyProvider : ITranslationProvider
{
    public string? GetTranslation(string key, CultureInfo culture) { ... }
    public IReadOnlyList<CultureInfo> GetSupportedCultures() { ... }
    public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture) { ... }
}
```

## Configuration

```csharp
var settings = LocalizationSettings.Default
    .WithDefaultCulture(CultureInfo.GetCultureInfo("en"))
    .WithFallbackToParentCulture(true)
    .WithMissingKeyBehavior(MissingKeyBehavior.ReturnKey)
    .WithKeyPrefix("myapp");
```

All settings are immutable — `With*()` methods return new instances.

## Entity Localization (Birko.Data.Localization)

For translating entity fields stored in the database (e.g., product names, category descriptions), use `Birko.Data.Localization`. It provides store decorator wrappers that transparently manage translations.

### How It Works

- Entities store default-language values in their own fields
- Translations for other languages live in a separate `EntityTranslationModel` store
- On **read**, the wrapper checks the current culture and overlays translations onto localizable fields
- On **create/update**, the wrapper persists translations for the current culture (if non-default)
- On **delete**, all associated translations are cleaned up

### 1. Implement ILocalizable

```csharp
public class Product : AbstractLogModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty; // not localized

    public IReadOnlyList<string> GetLocalizableFields()
        => new[] { nameof(Name), nameof(Description) };
}
```

### 2. Implement IEntityLocalizationContext

```csharp
public class HttpLocalizationContext : IEntityLocalizationContext
{
    private readonly IHttpContextAccessor _accessor;
    public HttpLocalizationContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;
    public CultureInfo DefaultCulture => new CultureInfo("en");
}
```

### 3. Wrap Your Store

```csharp
// Create the translation store (any Birko.Data backend)
var translationStore = new AsyncDataBaseBulkStore<MsSqlConnector, EntityTranslationModel>();

// Wrap the entity store
var localizedStore = new AsyncLocalizedStoreWrapper<IAsyncStore<Product>, Product>(
    productStore,
    translationStore,
    localizationContext);

// Reads automatically return translated fields
var product = await localizedStore.ReadAsync(productGuid);
```

### 4. Decorator Composition

```csharp
IAsyncStore<Product> store = productStore;
store = new AsyncTimestampStoreWrapper<...>(store, clock);
store = new AsyncAuditStoreWrapper<...>(store, auditContext);
store = new AsyncLocalizedStoreWrapper<...>(store, translationStore, locContext);
```

### 5. Your own translation model, and SQL

The wrappers take any model implementing `IEntityTranslation` (`EntityGuid`, `EntityType`, `FieldName`, `Culture`,
`Value`, `UpdatedAt`) — for instance one deriving from your tenant base, so tenant filtering and purge apply to
translations too. `EntityTranslationModel` is the default, used by the two-parameter wrapper names.

```csharp
public sealed class ProductTranslation : TenantEntity, IEntityTranslation { /* the six properties */ }

var products = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<Product>, Product, ProductTranslation>(
    productStore, productTranslationStore, locContext);
```

On MongoDB, create the translation indexes once at startup with `Birko.Data.Localization.MongoDB` (TASK-699): the unique
key and the `ValueHash` index. Without them a value lookup scans the collection (measured: `COLLSCAN`):

```csharp
await LocalizationMongoIndexes.EnsureAsync(translationStore);   // an AsyncMongoDBStore<EntityTranslationModel>
```

On SQL, map the table with `Birko.Data.Localization.SQL` before the first store use:

```csharp
var registry = new ModelMapRegistry();
LocalizationSqlMappings.Register(registry);                                   // the default model
LocalizationSqlMappings.Register<ProductTranslation>(registry, "ProductTranslations"); // or your own
registry.ApplyToDatabase();
```

A filter on a translated field matches an entity on its translation, or — when it has none in that culture — on its base value, so an untranslated product is still found by its slug. `==` and `IN` are looked up by `ValueHash` (SHA-256 of the value, part of `IEntityTranslation`), which the SQL mapping indexes.

The mapping declares a **unique** key over `(EntityGuid, EntityType, FieldName, Culture)`. Two writers saving one key at
once then leave one row: the second create is refused by the key and retried as an update. The key's string columns
are bounded (128, 128 and 85 characters), so it fits on all four providers, MySQL included (TASK-684).

### Available Wrappers

| Wrapper | Interface | Description |
|---------|-----------|-------------|
| `LocalizedStoreWrapper` | `IStore<T>` | Sync singular |
| `AsyncLocalizedStoreWrapper` | `IAsyncStore<T>` | Async singular |
| `LocalizedBulkStoreWrapper` | `IBulkStore<T>` | Sync bulk |
| `AsyncLocalizedBulkStoreWrapper` | `IAsyncBulkStore<T>` | Async bulk |

### EntityTranslationModel

| Property | Type | Description |
|----------|------|-------------|
| Guid | Guid? | Unique identifier (inherited) |
| EntityGuid | Guid | The entity this translation belongs to |
| EntityType | string | Type name (e.g., "Product") |
| FieldName | string | Property name (e.g., "Name") |
| Culture | string | Culture code (e.g., "sk") |
| Value | string | Translated value |
| UpdatedAt | DateTime? | Last modification |

### EntityTranslationFilter

Query builder with static factories:

```csharp
EntityTranslationFilter.ByEntity(entityGuid);
EntityTranslationFilter.ByEntityAndCulture(entityGuid, "sk");
EntityTranslationFilter.ByEntityFieldAndCulture(entityGuid, "Name", "sk");
EntityTranslationFilter.ByEntityType("Product");
EntityTranslationFilter.ByEntityTypeAndCulture("Product", "sk");
```

### 6. One transaction for the entity and its translations

Pass `SqlLocalizedWriteBoundary.For(entityStore, translationStore)` (Birko.Data.Localization.SQL) as the wrapper's last
argument. When both stores are SQL on one database, the entity write and its translation write commit or roll back
together, joining your `SqlUnitOfWork` if you opened one. Other pairings are best effort. A delete whose translation
cleanup fails then throws `TranslationCleanupException` naming the entities to clean with `RemoveEntitiesAsync`. See
the [pairing table](../../Birko.Data.Localization/README.md#atomicity-which-store-pairings-are-one-unit).

### 7. The explicit API

`IEntityTranslations<T>` gives explicit, batched access to translations for a given culture, beside the wrappers'
ambient one: one language for a list, every language of one entity, upsert and remove, slug → entity with the
base-value fallback, and "is this value taken in this language". Register it with
`services.AddEntityTranslations<Product>()`. See the
[project README](../../Birko.Data.Localization/README.md#6-the-explicit-api--ientitytranslationst) for an example per
method.

### 8. Broad matches

A filter on a translated field that matches more than 500 entities in its translation sets runs as several queries
over disjoint chunks of at most 500 GUIDs (TASK-686), so SQL Server's ~2 100-parameter limit is never reached. Reads,
counts, updates and deletes cover every chunk; ordered or paged reads sort and page the whole match in memory. A
wrapper over a plain `IStore` refuses such a filter. See the
[project README](../../Birko.Data.Localization/README.md#filters-on-localizable-fields).

### 9. Races on every provider

Two writers saving the same translation at once leave one row on every provider (TASK-698). A new row's Guid is
derived from its key, so the second writer's create is refused like any duplicate Guid and retried as an update.
Before, only SQL's unique key did this, and the other providers stored a duplicate row. Do not wrap the translation
store in a soft-delete decorator: the localized wrappers refuse it, because a soft-deleted translation keeps its key.
