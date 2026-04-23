# Birko.Data.Migrations

## Overview
Platform-agnostic database migration framework. Write migrations once and run them against any provider (SQL, MongoDB, ElasticSearch, RavenDB, CosmosDB, InfluxDB, TimescaleDB).

## Project Location
`C:\Source\Birko.Data.Migrations\`

## Components

### Core Interfaces
- `IMigration` — `Up(IMigrationContext context)`, `Down(IMigrationContext context)` with Version (long), Name, Description, CreatedAt
- `IMigrationContext` — `Schema` (ISchemaBuilder), `Data` (IDataMigrator), `Raw(Action<object>)`, `ProviderName`
- `IMigrationRunner` — Register, Migrate, Rollback, GetCurrentVersion
- `IMigrationStore` — RecordMigration, RemoveMigration, GetExecutedMigrations

### Base Classes
- `AbstractMigration` — Base class implementing IMigration
- `AbstractMigrationRunner` — Template method pattern for migration execution

### Context Abstractions
- `IMigrationContext` — Platform-agnostic context (Schema, Data, Raw, ProviderName)
- `IDataMigrator` — UpdateDocuments, DeleteDocuments, CountDocuments, CopyData, BulkInsert

### Supporting Types
- `MigrationDirection` — Up / Down enum
- `MigrationResult` — Execution result with executed migrations list
- `ExecutedMigration` — Record of an executed migration
- `MigrationException` — Thrown when a migration fails

## Creating a Migration

```csharp
using Birko.Data.Migrations;
using Birko.Data.Patterns.Schema;

public class CreateUsersTable : AbstractMigration
{
    public override long Version => 20260423_001;
    public override string Name => "CreateUsersTable";

    public override void Up(IMigrationContext context)
    {
        context.Schema.CreateCollection("Users", b => b
            .WithField("Id", FieldType.Guid, f => f.IsPrimary = true)
            .WithField("Email", FieldType.String, f => { f.MaxLength = 256; f.IsRequired = true; }));
    }

    public override void Down(IMigrationContext context)
    {
        context.Schema.DropCollection("Users");
    }
}
```

## Dependencies
- Birko.Data.Patterns (FieldType, FieldDescriptor, ISchemaBuilder, IIndexBuilder)

## Provider Projects
- [Birko.Data.Migrations.SQL](../Birko.Data.Migrations.SQL/CLAUDE.md)
- [Birko.Data.Migrations.MongoDB](../Birko.Data.Migrations.MongoDB/CLAUDE.md)
- [Birko.Data.Migrations.ElasticSearch](../Birko.Data.Migrations.ElasticSearch/CLAUDE.md)
- [Birko.Data.Migrations.RavenDB](../Birko.Data.Migrations.RavenDB/CLAUDE.md)
- [Birko.Data.Migrations.CosmosDB](../Birko.Data.Migrations.CosmosDB/CLAUDE.md)
- [Birko.Data.Migrations.InfluxDB](../Birko.Data.Migrations.InfluxDB/CLAUDE.md)
- [Birko.Data.Migrations.TimescaleDB](../Birko.Data.Migrations.TimescaleDB/CLAUDE.md)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
