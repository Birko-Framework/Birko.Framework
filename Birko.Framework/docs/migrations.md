# Migration Guide

## Overview

Birko.Data.Migrations provides a platform-agnostic migration framework. Write a migration once and run it against any supported provider — SQL, MongoDB, ElasticSearch, RavenDB, CosmosDB, InfluxDB, or TimescaleDB.

## Supported Providers

| Provider | Project | What to pass to runner |
|----------|---------|----------------------|
| SQL | `Birko.Data.Migrations.SQL` | `AbstractConnector` (from `store.Connector`) |
| MongoDB | `Birko.Data.Migrations.MongoDB` | `MongoDBClient` (from `store.Client`) |
| ElasticSearch | `Birko.Data.Migrations.ElasticSearch` | `ElasticClient` (from `store.Connector`) |
| RavenDB | `Birko.Data.Migrations.RavenDB` | `IDocumentStore` (from `store.DocumentStore`) |
| CosmosDB | `Birko.Data.Migrations.CosmosDB` | `Database` (from `store.Client`/`store.Container`) |
| InfluxDB | `Birko.Data.Migrations.InfluxDB` | `InfluxDBClient` (from `store.Client`) |
| TimescaleDB | `Birko.Data.Migrations.TimescaleDB` | `AbstractConnector` (from `store.Connector`) |

## Platform-Agnostic Migration

All migrations implement `IMigration` from `Birko.Data.Migrations` and receive an `IMigrationContext`:

```csharp
using Birko.Data.Migrations;
using Birko.Data.Patterns.Schema;

public class CreateUsersTable : AbstractMigration
{
    public override long Version => 20260423_001;
    public override string Name => "CreateUsersTable";

    public override void Up(IMigrationContext context)
    {
        context.Schema.CreateCollection("Users")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Email", FieldType.String, maxLength: 256)
            .WithField("Name", FieldType.String, maxLength: 256)
            .WithField("CreatedAt", FieldType.DateTime)
            .Build();

        context.Schema.CreateIndex("Users", "IX_Users_Email")
            .WithField("Email")
            .Unique()
            .Build();
    }

    public override void Down(IMigrationContext context)
    {
        context.Schema.DropCollection("Users");
    }
}
```

**Every `CreateCollection` / `CreateIndex` chain ends with `Build()`.** The builders emit nothing until it is called
(every provider defers an index, SQL defers a table), and the omission compiles. So the runner checks when a
migration's `Up` / `Down` returns: a chain that was never built fails the migration with a `MigrationException` naming
it (`CreateIndex("Users", "IX_Users_Email")`), and the migration is **not recorded**. Before TASK-627 it emitted
nothing and was recorded as applied, a schema change missed for good. The rule holds on providers that create a
collection eagerly too, so a migration that passes on one provider passes on all.

### IMigrationContext

The context provides three capabilities:

| Member | Type | Purpose |
|--------|------|---------|
| `Schema` | `ISchemaBuilder` | Create/drop collections, indexes, fields |
| `Data` | `IDataMigrator` | Update/delete/count documents, bulk insert, copy data |
| `Raw(Action<object>)` | Method | Escape hatch — access the native provider client |
| `ProviderName` | `string` | Identifies the provider (e.g., "SQL", "MongoDB") |

### ISchemaBuilder Operations

| Method | SQL Translation | NoSQL Behavior |
|--------|----------------|----------------|
| `CreateCollection(name)...Build()` | CREATE TABLE | Create collection/container/index |
| `DropCollection(name)` | DROP TABLE | Drop collection/container |
| `CollectionExists(name)` | Information schema check | Provider-specific check |
| `AddField(collection, field)` | ALTER TABLE ADD COLUMN | No-op (schema-less) |
| `DropField(collection, name)` | ALTER TABLE DROP COLUMN | No-op |
| `RenameField(collection, old, new)` | EXEC sp_rename / ALTER | $rename (MongoDB), no-op (others) |
| `CreateIndex(collection, name)...Build()` | CREATE INDEX | Create native index |
| `DropIndex(collection, name)` | DROP INDEX | Drop native index |

### IDataMigrator Operations

| Method | Description |
|--------|-------------|
| `UpdateDocuments(collection, filter, updates)` | Update matching documents |
| `DeleteDocuments(collection, filter)` | Delete matching documents |
| `CountDocuments(collection, filter)` | Count matching documents |
| `CopyData(source, target, transform)` | Copy documents between collections |
| `BulkInsert(collection, documents)` | Insert multiple documents |

**The filter** is a Mongo-style JSON object, read by one shared parser (`MigrationFilterTerms`) for SQL, ElasticSearch,
RavenDB and Cosmos DB (TASK-152): a field's value (equality) or an object of `$eq`, `$ne`, `$gt`, `$gte`, `$lt`, `$lte`.
Any other operator (`$in`, `$exists`, `$regex`, ...), an array or object value, and an ordering comparison with `null`
are refused with `NotSupportedException`; they used to become equality and hit the wrong rows. `null` means what it means
in MongoDB on every backend: `{"x": null}` matches a missing or null `x`, `{"x": {"$ne": null}}` one that has a value,
and `{"x": {"$ne": v}}` also matches a document without `x` (SQL's `x <> v` alone never matched NULL).

**What a backend cannot do, it refuses**, rather than reporting success: a `CopyData` transform on SQL, Cosmos DB and
InfluxDB (only MongoDB applies one); and on InfluxDB, `UpdateDocuments` (points are immutable), a filtered
`CountDocuments`, a delete from a bucket that does not exist, and a `BulkInsert` document with no numeric or boolean value
(a point with no field is not written).

### FieldDescriptor

Fields are described using `FieldDescriptor` from `Birko.Data.Patterns.Schema`:

```csharp
var field = new FieldDescriptor
{
    Name = "Email",
    Type = FieldType.String,
    MaxLength = 256,
    IsRequired = true,
    IsUnique = true
};
```

Available `FieldType` values: String, Integer, Long, Decimal, Double, Boolean, DateTime, Guid, Binary, Json.

### Raw Escape Hatch

For provider-specific operations that don't have a platform-agnostic equivalent:

```csharp
public override void Up(IMigrationContext context)
{
    if (context.ProviderName == "SQL")
    {
        context.Raw(obj =>
        {
            var connection = (DbConnection)obj;
            // Execute raw SQL
        });
    }
}
```

The `Raw()` parameter type varies by provider:

| Provider | Raw parameter type |
|----------|-------------------|
| SQL | `DbConnection` |
| MongoDB | `IMongoDatabase` |
| ElasticSearch | `ElasticClient` |
| RavenDB | `IDocumentStore` |
| CosmosDB | `Database` |
| InfluxDB | `InfluxDBClient` |

## Running Migrations

Each provider has its own runner that takes the store's native connector:

```csharp
// SQL
var runner = new SqlMigrationRunner(store.Connector);

// MongoDB
var runner = new MongoMigrationRunner(store.Client);

// ElasticSearch
var runner = new ElasticSearchMigrationRunner(store.Connector);

// RavenDB
var runner = new RavenMigrationRunner(store.DocumentStore);

// CosmosDB
var runner = new CosmosMigrationRunner(database);

// InfluxDB
var runner = new InfluxMigrationRunner(influxClient, "my-org");

// TimescaleDB
var runner = new TimescaleDBMigrationRunner(store.Connector);
```

**Which migrations run** is decided by the applied set, not by a version range (TASK-152): `Migrate()` runs every
registered migration not yet applied up to the target, including one whose version is below the current one (added
later from a merged branch), in version order. `Rollback(target)` runs `Down` only for applied migrations above the
target, never for one that was skipped.

### Registering Migrations

```csharp
runner.Register(new CreateUsersTable());
runner.Register(new AddEmailIndex());

// Run all pending
runner.Migrate();

// Rollback to a specific version
runner.RollbackTo(20260423_001);
```

## TimescaleDB-Specific Migrations

TimescaleDB extends the SQL context. For hypertable and compression operations, use `Raw()`:

```csharp
public override void Up(IMigrationContext context)
{
    context.Schema.CreateCollection("metrics", b => b
        .WithField("time", FieldType.DateTime, f => f.IsPrimary = true)
        .WithField("value", FieldType.Double));

    if (context.ProviderName == "TimescaleDB")
    {
        context.Raw(obj =>
        {
            var connection = (DbConnection)obj;
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT create_hypertable('metrics', 'time')";
            cmd.ExecuteNonQuery();
        });
    }
}
```

## SQL View Migrations

`Birko.Data.SQL.View.Migrations` integrates SQL View definitions with the Migration framework:

```csharp
using Birko.Data.SQL.View.Migrations;

public class AddCustomerOrdersView : AbstractMigration
{
    public override long Version => 20260423_002;
    public override string Name => "AddCustomerOrdersView";

    public override void Up(IMigrationContext context)
    {
        context.CreateView<CustomerOrderView>();
    }

    public override void Down(IMigrationContext context)
    {
        context.DropView("customer_orders_view");
    }
}
```

### API Reference

| Method | Description |
|--------|-------------|
| `context.CreateView<T>()` | Extension: create view from attributed class |
| `context.DropView(viewName)` | Extension: drop view |
| `ViewSqlGenerator.GenerateCreateViewSql<T>(quoteChar)` | Generate CREATE VIEW DDL |
| `ViewSqlGenerator.GenerateDropViewSql<T>()` | Generate DROP VIEW DDL |

## Model Mapping Integration

`FieldDescriptor` (Birko.Data.Patterns.Schema) is also used by the SQL model mapping framework. This means the same type describes fields for both migrations and model-to-table mapping:

```csharp
// In a mapping (Birko.Models.SQL)
public class CurrencyMapping : IModelMapping<Currency>
{
    public void Configure(ModelMap<Currency> map)
    {
        map.ToTable("Currencies")
            .HasPrimary(x => x.Guid)
            .HasUnique(x => x.Guid);

        map.Property(x => x.Code).HasMaxLength(8).IsUnique();
    }
}
```

`FieldBuilder<T>` provides the fluent API for configuring `FieldDescriptor` properties in mappings.

## Best Practices

1. **Versioning**: Use numeric versions (e.g., `20260423_001`) for ordering. Migrations run in ascending version order.
2. **Reversibility**: Implement `Down()` for rollback support where possible.
3. **Idempotency**: Use `CollectionExists()` guards and `IF EXISTS` patterns.
4. **Small steps**: Each migration should make one logical change.
5. **Provider checks**: Use `context.ProviderName` or try the agnostic API first — NoSQL providers silently skip inapplicable operations.
6. **Raw escape hatch**: Use `context.Raw()` only when the platform-agnostic API doesn't cover your needs.

## Architecture

```
Birko.Data.Patterns (FieldType, FieldDescriptor, ISchemaBuilder, IIndexBuilder)
  -> Birko.Data.Migrations (IMigration, IMigrationContext, IDataMigrator, AbstractMigration, AbstractMigrationRunner)
    -> Birko.Data.Migrations.SQL (SqlMigrationContext, SqlSchemaBuilder, SqlDataMigrator, SqlMigrationRunner)
    -> Birko.Data.Migrations.MongoDB (MongoMigrationContext, MongoSchemaBuilder, MongoDataMigrator, MongoMigrationRunner)
    -> Birko.Data.Migrations.ElasticSearch (ElasticSearchMigrationContext, ...)
    -> Birko.Data.Migrations.RavenDB (RavenDBMigrationContext, ...)
    -> Birko.Data.Migrations.CosmosDB (CosmosDBMigrationContext, ...)
    -> Birko.Data.Migrations.InfluxDB (InfluxMigrationContext, ...)
    -> Birko.Data.Migrations.TimescaleDB (TimescaleDBMigrationContext extends SqlMigrationContext, TimescaleDBMigrationRunner)
```
