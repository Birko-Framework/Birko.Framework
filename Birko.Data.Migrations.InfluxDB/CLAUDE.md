# Birko.Data.Migrations.InfluxDB

## Overview
InfluxDB-specific migration framework for managing buckets, retention policies, and data lifecycle.

## Project Location
`C:\Source\Birko.Data.Migrations.InfluxDB\`

## Components

### Migration Base Class
- `InfluxMigration` - Extends `AbstractMigration` with `InfluxDBClient` and organization parameters
  - Helpers: `CreateBucket()`, `DeleteBucket()`, `FindBucket()`, `DeleteData()`, `CreateBucketWithRetention()`, `BucketExists()`

### Store
- `InfluxMigrationStore` - Implements `IMigrationStore`, stores migration metadata in InfluxDB measurements using Flux queries

### Runner
- `InfluxMigrationRunner` - Extends `AbstractMigrationRunner` with `InfluxDBClient` and organization fields

## Dependencies
- Birko.Data.Migrations
- Birko.Data.InfluxDB
- InfluxDB.Client

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
