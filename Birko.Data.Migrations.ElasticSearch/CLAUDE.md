# Birko.Data.Migrations.ElasticSearch

## Overview
Elasticsearch-specific migration framework for managing index schemas, aliases, and mappings.

## Project Location
`C:\Source\Birko.Data.Migrations.ElasticSearch\`

## Components

### Migration Base Class
- `ElasticSearchMigration` - Extends `AbstractMigration` with `ElasticClient`-specific methods
  - Abstract: `Up(ElasticClient)`, `Down(ElasticClient)`
  - Helpers: `CreateIndex()`, `DeleteIndex()`, `CreateAlias()`, `DeleteAlias()`, `Reindex()`, `UpdateMapping()`, `PutTemplate()`, `DeleteTemplate()`

### Settings
- `ElasticSearchMigrationSettings` - Extends `ElasticSearch.Stores.Settings`
  - `MigrationsIndex`, `UseAliases`, `NumberOfShards`, `NumberOfReplicas`

### Store
- `ElasticSearchMigrationStore` - Implements `IMigrationStore`, stores migration state in Elasticsearch index using `MigrationDocument`

### Runner
- `ElasticSearchMigrationRunner` - Extends `AbstractMigrationRunner`, executes `ElasticSearchMigration` instances

## Dependencies
- Birko.Data.Migrations
- Birko.Data.ElasticSearch
- NEST / Elasticsearch.Net

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
