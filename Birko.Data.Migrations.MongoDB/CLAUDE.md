# Birko.Data.Migrations.MongoDB

## Overview
MongoDB-specific migration framework for managing collections, indexes, and validation rules.

## Project Location
`C:\Source\Birko.Data.Migrations.MongoDB\`

## Components

### Migration Base Class
- `MongoMigration` - Extends `AbstractMigration` with `IMongoClient` and `IMongoDatabase` parameters
  - Helpers: `CreateCollection()`, `DropCollection()`, `CreateIndex()` (2 overloads), `DropIndex()`, `DropAllIndexes()`, `RenameCollection()`, `UpdateDocuments()`, `RunCommand()`, `SetValidationRule()`, `CollectionExists()`

### Settings
- `MongoMigrationSettings` - Extends `MongoDB.Stores.Settings`
  - `MigrationsCollection`, `UseSession` properties

### Store
- `MongoMigrationStore` - Implements `IMigrationStore` with `MigrationDocument`, handles transactions

### Runner
- `MongoMigrationRunner` - Extends `AbstractMigrationRunner`, manages MongoDB sessions and transactions

## Dependencies
- Birko.Data.Migrations
- Birko.Data.MongoDB
- MongoDB.Driver

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
