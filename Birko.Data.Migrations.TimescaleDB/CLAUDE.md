# Birko.Data.Migrations.TimescaleDB

## Overview
TimescaleDB migration backend extending SQL migrations. Uses AbstractConnector (PostgreSQL). TimescaleDB-specific operations (hypertables, compression policies) use the Raw() escape hatch.

## Project Location
`Birko.Data.Migrations.TimescaleDB/`

## Components

### Runner
- `TimescaleDBMigrationRunner` — Extends SqlMigrationRunner. Takes `AbstractConnector` (from `store.Connector`). Overrides context creation to provide TimescaleDBMigrationContext.

### Context
- `TimescaleDBMigrationContext` — Extends SqlMigrationContext. ProviderName is "TimescaleDB". Provides Connection and Transaction properties for TimescaleDB-specific SQL via Raw().

## TimescaleDB-Specific Operations

Use `context.Raw()` for operations not covered by the platform-agnostic API:

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

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Migrations.SQL
- Birko.Data.Patterns
- Birko.Data.SQL

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
