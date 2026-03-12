# Birko.Data.Migrations.TimescaleDB

## Overview
TimescaleDB-specific migration framework extending SQL migrations with hypertable, compression, and continuous aggregate support.

## Project Location
`C:\Source\Birko.Data.Migrations.TimescaleDB\`

## Components

### Migration Base Class
- `TimescaleDBMigration` - Extends `SQL.SqlMigration` with TimescaleDB-specific methods
  - Hypertables: `CreateHypertable()`, `CreateHypertableWithSpace()`, `IsHypertable()`, `GetChunkInterval()`
  - Policies: `AddCompressionPolicy()`, `AddRetentionPolicy()`, `RemoveCompressionPolicy()`, `RemoveRetentionPolicy()`
  - Aggregates: `CreateContinuousAggregate()`, `RefreshContinuousAggregate()`
  - Internal: `EnsureTimescaleDBExtension()`

### Runner
- `TimescaleDBMigrationRunner` - Extends `SQL.SqlMigrationRunner`

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Migrations.SQL
- Birko.Data.TimescaleDB

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
