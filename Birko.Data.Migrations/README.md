# Birko.Data.Migrations

Database migration framework for managing schema changes in the Birko Framework.

## Features

- Reversible migrations with Up/Down methods
- Date-based versioning (YYYY.MM.DD format)
- Migration runner with automatic ordering
- Multi-database support via provider-specific packages

## Installation

```bash
dotnet add package Birko.Data.Migrations
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (Settings)

## Usage

### Create a Migration

```csharp
using Birko.Data.Migrations;

public class Migration_2024_01_01_CreateUsersTable : Migration
{
    public override string Name => "CreateUsersTable";
    public override string Version => "2024.01.01";

    public override void Up()
    {
        // Apply schema changes
    }

    public override void Down()
    {
        // Rollback schema changes
    }
}
```

### Run Migrations

```csharp
var runner = new MigrationRunner(settings);
runner.RunMigrations();
```

## API Reference

- **Migration** - Abstract base class with `Name`, `Version`, `Up()`, `Down()`
- **MigrationRunner** - Discovers and executes migrations in version order
- **MigrationSettings** - Connection string, database name, assembly

## Provider-Specific Packages

- [Birko.Data.Migrations.SQL](../Birko.Data.Migrations.SQL/) - SQL databases
- [Birko.Data.Migrations.ElasticSearch](../Birko.Data.Migrations.ElasticSearch/) - Elasticsearch
- [Birko.Data.Migrations.MongoDB](../Birko.Data.Migrations.MongoDB/) - MongoDB
- [Birko.Data.Migrations.RavenDB](../Birko.Data.Migrations.RavenDB/) - RavenDB
- [Birko.Data.Migrations.InfluxDB](../Birko.Data.Migrations.InfluxDB/) - InfluxDB
- [Birko.Data.Migrations.TimescaleDB](../Birko.Data.Migrations.TimescaleDB/) - TimescaleDB

## License

Part of the Birko Framework.
