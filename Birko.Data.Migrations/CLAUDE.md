# Birko.Data.Migrations

## Overview
Database migration framework for managing schema changes across different storage backends.

## Project Location
`C:\Source\Birko.Data.Migrations\`

## Purpose
- Version control for database schemas
- Automated schema updates
- Rollback support
- Multi-database support

## Migration Structure

```csharp
using Birko.Data.Migrations;

public abstract class Migration
{
    public abstract string Name { get; }
    public abstract string Version { get; }

    public abstract void Up();
    public abstract void Down();
}
```

## Creating a Migration

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

## Running Migrations

```csharp
using Birko.Data.Migrations;

var runner = new MigrationRunner(settings);
runner.RunMigrations();
```

## Dependencies
- Birko.Data.Core

## Provider-Specific Migrations

Different providers have their own migration projects:
- [Birko.Data.Migrations.SQL](../Birko.Data.Migrations.SQL/CLAUDE.md) - SQL migrations
- [Birko.Data.Migrations.ElasticSearch](../Birko.Data.Migrations.ElasticSearch/CLAUDE.md) - Elasticsearch migrations
- [Birko.Data.Migrations.MongoDB](../Birko.Data.Migrations.MongoDB/CLAUDE.md) - MongoDB migrations
- [Birko.Data.Migrations.RavenDB](../Birko.Data.Migrations.RavenDB/CLAUDE.md) - RavenDB migrations
- [Birko.Data.Migrations.InfluxDB](../Birko.Data.Migrations.InfluxDB/CLAUDE.md) - InfluxDB migrations
- [Birko.Data.Migrations.TimescaleDB](../Birko.Data.Migrations.TimescaleDB/CLAUDE.md) - TimescaleDB migrations

## Versioning

Use date-based versioning:
- Format: `YYYY.MM.DD`
- Example: `2024.01.15`
- Optional suffix for multiple migrations: `2024.01.15.1`, `2024.01.15.2`

## Best Practices

1. **Always implement Down()** - Make migrations reversible
2. **Test migrations** - Test both Up and Down
3. **Idempotent** - Migrations should be runnable multiple times
4. **Small changes** - Keep migrations focused and small
5. **Backup first** - Always backup before running migrations
6. **Version order** - Ensure versions are in chronological order

## Settings

```csharp
public class MigrationSettings
{
    public string ConnectionString { get; set; }
    public string DatabaseName { get; set; }
    public Assembly MigrationsAssembly { get; set; }
}
```

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
