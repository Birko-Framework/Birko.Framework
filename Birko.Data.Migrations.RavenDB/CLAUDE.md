# Birko.Data.Migrations.RavenDB

## Overview
RavenDB-specific migration framework for managing indexes, documents, and bulk operations.

## Project Location
`C:\Source\Birko.Data.Migrations.RavenDB\`

## Components

### Migration Base Class
- `RavenMigration` - Extends `AbstractMigration` with `IDocumentStore` parameter
  - Helpers: `CreateIndex()`, `DeployIndex()`, `DeleteIndex()`, `UpdateDocuments()`, `DeleteDocumentsByQuery()`, `LoadDocument()`, `StoreDocument()`, `DeleteDocument()`, `DocumentExists()`, `BulkInsert()`

### Store
- `RavenMigrationStore` - Implements `IMigrationStore`, stores state in single RavenDB document
  - Internal: `MigrationsStateDocument`, `MigrationRecord`

### Runner
- `RavenMigrationRunner` - Extends `AbstractMigrationRunner` with `IDocumentStore` field

## Dependencies
- Birko.Data.Migrations
- Birko.Data.RavenDB
- RavenDB.Client

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
