# Birko.Data.Localization.SQL

SQL schema for [Birko.Data.Localization](../Birko.Data.Localization/README.md)'s translation table: the table and its
unique key, for the default `EntityTranslationModel` or a model of your own.

## Features

- One fluent mapping for any `IEntityTranslation` model, in a table you name.
- A **unique** key over `(EntityGuid, EntityType, FieldName, Culture)`: two writers saving one key at once leave one row
  (the second create is refused and retried as an update by the localization wrappers).
- No index on `Value`, so a translation's length is not capped by an index key; exact-value lookups (slugs) use an index on `ValueHash` instead.
- `SqlLocalizedWriteBoundary`: the entity write and its translation write in one transaction when both stores are on
  one database (TASK-683).

## Usage

```csharp
var registry = new ModelMapRegistry();
LocalizationSqlMappings.Register(registry);                                        // EntityTranslationModel
LocalizationSqlMappings.Register<ProductTranslation>(registry, "ProductTranslations"); // your own model
registry.ApplyToDatabase();

var translations = new AsyncDataBaseBulkStore<PostgreSQLConnector, ProductTranslation>();
translations.SetSettings(settings);
var products = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<Product>, Product, ProductTranslation>(
    productStore, translations, localizationContext);
```

A tenant-aware model just implements the interface on top of its tenant base:

```csharp
public sealed class ProductTranslation : TenantEntity, IEntityTranslation
{
    public Guid EntityGuid { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Culture { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}
```

### One transaction for the entity and its translations

```csharp
var boundary = SqlLocalizedWriteBoundary.For(productStore, translations);                   // best effort if not one database
var strict   = SqlLocalizedWriteBoundary.For(productStore, translations, requireAtomic: true); // throws instead
var products = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<Product>, Product, ProductTranslation>(
    productStore, translations, localizationContext, boundary);
var api = new EntityTranslations<Product, ProductTranslation>(productStore, translations, localizationContext, boundary);
```

`For` follows each store's decorator chain to its SQL store and compares `Connector.Settings.GetId()`. When the IDs
match:
- A write that touches both stores commits or rolls back as one.
- It joins a `SqlUnitOfWork` you opened, and a failure marks yours rollback-only.
- In its own transaction, a unique-key race is retried once by re-running the whole write.

See [the pairing table](../Birko.Data.Localization/README.md#atomicity-which-store-pairings-are-one-unit).

**MySQL:** the key is 3 204 bytes under utf8mb4, over InnoDB's 3 072, so it is not created there yet (reported in
`IndexCreationFailures`; TASK-684).

## Running tests

```sh
dotnet test tests/Birko.Data.Localization.SQL.Tests --nologo
```

Set `BIRKO_PG_HOST`, `BIRKO_MSSQL_HOST`, `BIRKO_MYSQL_HOST` (see `.github/workflows/live-tests.yml`) to run the live
providers too.

## License

MIT — see the repository's [LICENSE](../LICENSE).
