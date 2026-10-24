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
    public string ValueHash { get; set; } = string.Empty;
    // UpdatedAt comes from the log model under TenantEntity: DateTime, as the interface declares (TASK-767).
    // A base on a bare AbstractModel declares `public DateTime UpdatedAt { get; set; }` itself.
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
- It joins a `SqlUnitOfWork` you opened, and a failure marks yours rollback-only. A write a decorator above retries
  (`RetryableWrite`) runs in a savepoint instead: a failure is rolled back to it and your unit stays committable
  (TASK-697).
- In its own transaction, a unique-key race is retried once by re-running the whole write.

See [the pairing table](../Birko.Data.Localization/README.md#atomicity-which-store-pairings-are-one-unit).

**Key column lengths** (TASK-684): `EntityType` and `FieldName` are 128 characters, and `Culture` is 85
(`CultureInfo.Name`, e.g. `sk-SK`). That keeps the key at 1 508 bytes on MySQL utf8mb4, under InnoDB's 3 072. A longer
value is refused by PostgreSQL, SQL Server and MySQL. MySQL refuses only under a strict `sql_mode`
(`STRICT_TRANS_TABLES`, the default); without it, MySQL truncates the value with a warning. SQLite does not enforce
lengths.

**A table created before TASK-684** keeps its 255-character columns: schema-ensure never alters a column.
- **MySQL:** the key is still refused (`IndexCreationFailures`).
- **PostgreSQL and SQL Server:** the key works.
- **Every provider:** `DetectDrift` reports the three columns, so `SchemaDriftHealthCheck` reads Degraded until they
  are narrowed.

To narrow them, for your table name:
1. Remove any rows that duplicate a key. On MySQL, two writers may have stored both before the key existed.
2. Alter the three columns:
   - **MySQL:** `ALTER TABLE <table> MODIFY EntityType VARCHAR(128) NOT NULL, MODIFY FieldName VARCHAR(128) NOT NULL,
     MODIFY Culture VARCHAR(85) NOT NULL`
   - **PostgreSQL:** `ALTER TABLE "<table>" ALTER COLUMN entitytype TYPE VARCHAR(128), ALTER COLUMN fieldname TYPE
     VARCHAR(128), ALTER COLUMN culture TYPE VARCHAR(85)`
   - **SQL Server:** drop `UX_<table>_Key` first (a column in an index cannot be altered), then
     `ALTER TABLE [<table>] ALTER COLUMN EntityType NVARCHAR(128) NOT NULL`, and the same for `FieldName` (128) and
     `Culture` (85).
3. The next schema-ensure creates the key, or recreates it.

**A table created before TASK-767** keeps `UpdatedAt` nullable; the model now declares it `DateTime`, so the column is
declared `NOT NULL`.
- `DetectDrift` reports `UpdatedAt` as a nullability mismatch (measured on MySQL).
- **A row whose `UpdatedAt` is NULL fails every read that includes it** (measured on MySQL: `InvalidCastException`,
  `DBNull` to `DateTime`). The wrappers always stamp it, so only rows written around them can be NULL. Fill them:
  `UPDATE <table> SET UpdatedAt = CURRENT_TIMESTAMP WHERE UpdatedAt IS NULL`. Making the column `NOT NULL` as well clears
  the drift.

## Running tests

```sh
dotnet test tests/Birko.Data.Localization.SQL.Tests --nologo
```

Set `BIRKO_PG_HOST`, `BIRKO_MSSQL_HOST`, `BIRKO_MYSQL_HOST` (see `.github/workflows/live-tests.yml`) to run the live
providers too.

## License

MIT — see the repository's [LICENSE](../LICENSE).
