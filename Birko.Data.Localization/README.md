# Birko.Data.Localization

Entity-level localization for Birko.Data stores. Provides store decorator wrappers that automatically translate localizable entity fields (e.g., Name, Description) based on the current culture.

## Features

- **ILocalizable interface** — Mark entities with translatable fields
- **IEntityTranslation** — The translation row contract; any model implementing it can be the translation store's type (TASK-680)
- **EntityTranslationModel** — The default translation row, keyed by (EntityGuid, EntityType, FieldName, Culture)
- **Store wrappers** — Transparent localization via decorator pattern (sync and async, singular and bulk)
- **IEntityLocalizationContext** — Pluggable culture context for determining current/default language
- **IEntityTranslations** — Explicit, batched translations for a given culture: get, every culture, set, remove, find by value, uniqueness, localize (TASK-682)
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

### 6. The explicit API — `IEntityTranslations<T>`

The wrappers follow the ambient culture. When code needs a given language per call, every language of one entity, a
lookup by a translated value or a uniqueness check, use `IEntityTranslations<T>`. It is built from the **entity store
itself, not the localized wrapper**, because lookups read base values through it. A localized wrapper anywhere in its
decorator chain is refused, and tenant or soft-delete wrappers are fine. It also takes the translation store and the
context, of which only `DefaultCulture` is read:

With `Slug` declared beside `Name` in `GetLocalizableFields()`:

```csharp
IEntityTranslations<Product> translations = new EntityTranslations<Product>(productStore, translationStore, context);
var sk = new CultureInfo("sk");

// One language for a list: id → field → value, one read per 500 ids.
var names = await translations.GetAsync(productIds, sk);

// Every language of one entity, for an editor: culture → field → value, one read.
var all = await translations.GetAllCulturesAsync(productId);

// Save one language: one read + at most 3 bulk calls; null removes that field's translation.
await translations.SetAsync(productId, sk, new Dictionary<string, string?> { ["Name"] = "Stolička", ["Slug"] = "stolicka" });

// Drop a language, or purge what deleted entities left behind.
await translations.RemoveCultureAsync(productId, sk);
await translations.RemoveEntitiesAsync(deletedIds);

// Slug → entity, with fallback: an untranslated product is found by its base slug.
Guid? id = await translations.FindEntityAsync("Slug", sk, "stolicka");

// Is this slug taken in Slovak by another product? Counts a base slug an untranslated product answers with.
bool taken = await translations.ExistsAsync("Slug", sk, "stolicka", exceptEntityGuid: productId);

// Apply a language to entities you already loaded: detached copies, one read.
IReadOnlyList<Product> shown = await translations.LocalizeAsync(products, sk);
```

- **Cost:** a fixed number of queries, however many fields are involved. `GetAsync` and `LocalizeAsync` take one
  translation read per 500 entities and `GetAllCulturesAsync` one read. `FindEntityAsync` and `ExistsAsync` take at
  most four on a non-default culture: a hash lookup, the base-value candidates, their translations (one more read per
  further 500 candidates), and the final read.
- **Default culture:** its values are the entity's own columns. `SetAsync` refuses it, the reads return translation
  rows only, and `FindEntityAsync` / `ExistsAsync` test the base column.
- **Refused with `ArgumentException`:** a field the type does not declare as localizable, an empty id, a culture with
  no name (the invariant culture), and the default culture for `SetAsync` / `RemoveCultureAsync`.
- **Ambiguous lookup:** if more than one entity matches, `FindEntityAsync` throws `InvalidOperationException` naming
  two of them, rather than picking one.
- **Limits** are the filters' limits (above): the base-value fallback follows the entity store's collation. Each
  write is one unit through the boundary you pass. An entity write plus `SetAsync` is one unit only inside your own
  `SqlUnitOfWork` (see Atomicity below).

**Dependency injection:**

```csharp
services.AddEntityTranslations<Product>();                        // EntityTranslationModel, Scoped
services.AddEntityTranslations<Product, ShopTranslation>();       // your own model
services.AddEntityTranslations<Product, EntityTranslationModel>(  // when the container's IAsyncBulkStore<Product>
    sp => sp.GetRequiredService<ProductStore>(),                  // is the localized wrapper, pass the store it wraps
    sp => sp.GetRequiredService<IAsyncBulkStore<EntityTranslationModel>>());
```

The extension needs `Microsoft.Extensions.DependencyInjection.Abstractions`. A consumer that imports
`Birko.Data.Repositories` has it already. Any other consumer adds the package.

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore, IStoreWrapper)
- Microsoft.Extensions.DependencyInjection.Abstractions — only for `AddEntityTranslations`; consumer-supplied

## Filter-Based Bulk Operations

`Delete(filter)` never uses the inner store's native filter delete: it reads the matching entities, deletes them, and then deletes their translations, so the cascade happens. Filters on all bulk operations are translated as above. Under a non-default culture, the `Action<T>` overload shows the action the translated values, keeps the base columns on the default culture, and writes translations only for the localizable fields the action changed, after the inner store's update succeeds. A `PropertyUpdate` that sets a localizable field goes through the same path. Whether the entity write and the translation write are one unit depends on the boundary (below).

## Atomicity: which store pairings are one unit

Every write that touches both stores runs through an `ILocalizedWriteBoundary` (TASK-683). Pass one as the wrappers' or
`EntityTranslations`' last constructor argument. Without one, writes are best effort.

| Pairing | Atomic? |
|---|---|
| Both stores SQL on one database, with `SqlLocalizedWriteBoundary.For(entityStore, translationStore)` ([Birko.Data.Localization.SQL](../Birko.Data.Localization.SQL/README.md)) | **Yes**, on SQLite, PostgreSQL, SQL Server and MySQL. A failed translation write rolls the entity write back. |
| Same pairing, no boundary, inside your own `SqlUnitOfWork` | **Yes**: both stores join your transaction. |
| Same pairing, neither | No |
| SQL stores on two databases (different `Settings.GetId()`) | No |
| InMemory, JSON, XML, MongoDB, RavenDB, Cosmos DB, Elasticsearch | No |
| A chain with a decorator that opens its own connection | Only as wide as that decorator (rule 6) |

**When the write is not atomic:**
- A failure after the entity write leaves the entity written.
- A filter update that fails part way still saves the translations of the entities it already wrote.
- A delete removes the entity first, then its translations. If that cleanup fails, the delete throws
  `TranslationCleanupException`, whose `RemainingEntityGuids` lists the entities whose translations remain. Clear them
  with `IEntityTranslations.RemoveEntitiesAsync`, which is safe to repeat.

**Inside an atomic boundary:**
- A unique-key race on a translation (another writer stored the same key first) rolls back and re-runs the whole write
  once, in a new transaction. A filter update's action runs again too.
- Inside your own `SqlUnitOfWork` the boundary joins instead, and does not retry. On PostgreSQL the race then reaches
  you as `StoreUniqueViolationException`, because PostgreSQL aborts the transaction. Any failure marks your unit of
  work rollback-only.


## License

MIT — see [License.md](License.md)
