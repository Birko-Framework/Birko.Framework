# Birko.Data.Localization

Entity-level localization for Birko.Data stores. Provides store decorator wrappers that automatically translate localizable entity fields (e.g., Name, Description) based on the current culture.

## Features

- **ILocalizable interface** — Mark entities with translatable fields
- **IEntityTranslation** — The translation row contract; any model implementing it can be the translation store's type (TASK-680)
- **EntityTranslationModel** — The default translation row, keyed by (EntityGuid, EntityType, FieldName, Culture)
- **Store wrappers** — Transparent localization via decorator pattern (sync and async, singular and bulk)
- **IEntityLocalizationContext** — Pluggable culture context for determining current/default language
- **EntityTranslationFilter** — Query builder for translation lookups on the default `EntityTranslationModel` (the wrappers build their own filters for any model)

## How It Works

1. Entity stores the default-language values in its own fields
2. Translations for other languages are stored in a separate `EntityTranslationModel` store
3. On **read**, the wrapper checks the current culture — if it differs from the default, it fetches translations and overwrites the entity's localizable fields (on a detached copy). A list is translated with one translation query per 500 entities, not one per entity
4. On **create/update**, the wrapper persists translations for the current culture (if non-default). Setting a localizable field to `null` removes that field's translation, so reads fall back to the base value; an empty string is stored as a translation. One write call costs one read of the existing rows (per 500 entities) and at most one bulk delete, one bulk update and one bulk create, however many entities and fields it carries. If a unique key refuses the create because another writer stored one of the keys in between, those rows are updated instead. A filter-based update that fails part way still saves the translations of the entities it already wrote, then rethrows the original error
5. On **delete**, all associated translations are removed, as one delete per 500 entities
6. `Init` initialises the translation store too; `Destroy` destroys the entity store and deletes this entity type's translation rows (only this type's: the translation store is shared)

Every name `GetLocalizableFields()` returns must be a public `string` property with a public getter and setter. Anything else — a typo, a renamed property, a `decimal` — is refused with `InvalidOperationException` when the wrapper is constructed. A translation row belongs to one `(EntityGuid, EntityType, FieldName, Culture)`, where `EntityType` is the store's type name.

## Filters on localizable fields

Under a non-default culture, a filter condition on a localizable field is answered from the translation rows. Supported: `==`, `!=`, `Contains`, `StartsWith` and `EndsWith` on the field itself, against a value (a `StringComparison` argument is honoured), and `values.Contains(x.Field)` (IN), joined by `&&`.

**Fallback:** an entity with a translation for that field in the current culture is matched only on the translation; an entity without one is matched on its base (default-culture) value. So an untranslated product is still found by its slug.

**Cost:** `==` and `IN` look translation rows up by `ValueHash` (SHA-256 of the value) and read only the matching rows; the other shapes read the field's translations in that culture and test them in memory. The fallback reads the entity store's rows that match on the base column, plus their translations in one batched read — for `!=` that can be most of the table.

**Limits:** translations match exactly (ordinal), while the base-column fallback is evaluated by the entity store under its collation — case-insensitive by default on SQL Server and MySQL, so `Name == "lavica"` can find an untranslated "Lavica" there but never a translated one. An IN over a set with a non-ordinal comparer is refused for that reason. A translation row stored without its `ValueHash` (written around the wrappers by a model that does not compute it, or before the column existed) is still found, by a read of the field's unhashed rows; backfill the column to keep lookups cheap. Ordering by a localizable field sorts the translated text in memory, collated with the current culture — so it loads every matching entity, not just the page; an order-by naming a property that doesn't exist, or a negative offset or limit, is refused.

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

### 5. Your own translation model (tenant-aware consumers)

Each wrapper has a three-parameter form taking the translation model: `AsyncLocalizedBulkStoreWrapper<TStore, T, TTranslation>`
with `TTranslation : AbstractModel, IEntityTranslation` (its members public properties, not an explicit implementation — checked when the wrapper is built). A tenant-aware consumer implements `IEntityTranslation` on
a model deriving from its tenant base, so its tenant filter, soft delete and purge apply to translations too. The
two-parameter names are the same wrappers with `EntityTranslationModel`. For SQL, map the table and its unique key with
[Birko.Data.Localization.SQL](../Birko.Data.Localization.SQL/README.md).

When a store enforces the key, two writers saving one `(entity, type, field, culture)` at once leave one row: the second
create is refused (`StoreUniqueViolationException`) and retried as an update.

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore, IStoreWrapper)

## Filter-Based Bulk Operations

`Delete(filter)` never uses the inner store's native filter delete: it reads the matching entities, deletes them, and then deletes their translations, so the cascade happens. Filters on all bulk operations are translated as above. Under a non-default culture, the `Action<T>` overload shows the action the translated values, keeps the base columns on the default culture, and writes translations only for the localizable fields the action changed, after the inner store's update succeeds. A `PropertyUpdate` that sets a localizable field goes through the same path. The entity write and the translation write are not atomic yet (TASK-683).

## License

MIT — see [License.md](License.md)
