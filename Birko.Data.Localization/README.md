# Birko.Data.Localization

Entity-level localization for Birko.Data stores. Provides store decorator wrappers that automatically translate localizable entity fields (e.g., Name, Description) based on the current culture.

## Features

- **ILocalizable interface** — Mark entities with translatable fields
- **EntityTranslationModel** — Separate translation table keyed by (EntityGuid, FieldName, Culture)
- **Store wrappers** — Transparent localization via decorator pattern (sync and async, singular and bulk)
- **IEntityLocalizationContext** — Pluggable culture context for determining current/default language
- **EntityTranslationFilter** — Query builder for translation lookups

## How It Works

1. Entity stores the default-language values in its own fields
2. Translations for other languages are stored in a separate `EntityTranslationModel` store
3. On **read**, the wrapper checks the current culture — if it differs from the default, it fetches translations and overwrites the entity's localizable fields
4. On **create/update**, the wrapper persists translations for the current culture (if non-default). Setting a localizable field to `null` removes that field's translation, so reads fall back to the base value; an empty string is stored as a translation
5. On **delete**, all associated translations are removed

## Filters on localizable fields

Under a non-default culture, a filter condition on a localizable field is answered from the translation rows. Supported: `==`, `!=`, `Contains`, `StartsWith` and `EndsWith` on the field itself, against a value (a `StringComparison` argument is honoured), joined by `&&`. Ordering by a localizable field sorts the translated text in memory, collated with the current culture.

Anything else that reads a localizable field throws `NotSupportedException` instead of silently testing the default-culture column: a condition inside `||` or `!`, a chained call (`x.Name.ToLower()`), a static call (`string.IsNullOrEmpty(x.Name)`) or a comparison with another column. `x.Name == null` / `!= null` is the one exception: it tests the base column.

## Usage

### 1. Implement ILocalizable on your model

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
    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;
    public CultureInfo DefaultCulture => new CultureInfo("en");
}
```

### 3. Wrap your store

```csharp
// Async
var localizedStore = new AsyncLocalizedStoreWrapper<IAsyncStore<Product>, Product>(
    innerStore,
    translationStore,
    localizationContext);

// Read in Slovak → automatically returns translated Name/Description
var product = await localizedStore.ReadAsync(productGuid);
```

### 4. Decorator composition

```csharp
// Compose with other wrappers
IAsyncStore<Product> store = productStore;
store = new AsyncTimestampStoreWrapper<...>(store, clock);
store = new AsyncAuditStoreWrapper<...>(store, auditContext);
store = new AsyncLocalizedStoreWrapper<...>(store, translationStore, locContext);
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore, IStoreWrapper)

## Filter-Based Bulk Operations

Filters on all bulk operations are translated as above. Under a non-default culture, the `Action<T>` overload shows the action the translated values, keeps the base columns on the default culture, and writes translations only for the localizable fields the action changed, after the inner store's update succeeds. A `PropertyUpdate` that sets a localizable field goes through the same path. The entity write and the translation write are not atomic yet (TASK-683).

## License

MIT — see [License.md](License.md)
