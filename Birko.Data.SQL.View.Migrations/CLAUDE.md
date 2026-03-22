# Birko.Data.SQL.View.Migrations

## Overview
Integration layer connecting SQL View definitions (from Birko.Data.SQL.View attributes) with the Migration framework (Birko.Data.Migrations.SQL). Allows views to be created and dropped as part of versioned database migrations.

## Project Location
`C:\Source\Birko.Data.SQL.View.Migrations\`

## Components

### ViewSqlGenerator
Static class for generating DDL strings from view attributes:
- `GenerateCreateViewSql<T>(quoteChar)` — reads `ViewAttribute`, `ViewColumnAttribute`, `ViewJoinAttribute`, and `ViewFilterAttribute` to produce a complete CREATE VIEW statement
- `GenerateDropViewSql<T>()` — produces DROP VIEW IF EXISTS statement
- `quoteChar` parameter — tuple `(string open, string close)` for provider-specific identifier quoting (e.g., `("\"","\"")` for PostgreSQL, `("[","]")` for MSSQL, `` ("`","`") `` for MySQL)

### ViewMigrationExtensions
Extension methods on `SqlMigration`:
- `CreateView<T>()` — generates and executes CREATE VIEW within the migration
- `DropView(viewName)` — generates and executes DROP VIEW IF EXISTS within the migration

## Dependencies
- Birko.Data.SQL.View (view attributes, ViewDefinition)
- Birko.Data.Migrations.SQL (SqlMigration base class)

## Key Notes
- ViewSqlGenerator produces static SQL strings — no database connection required at generation time
- The quoteChar parameter ensures generated DDL is compatible with the target database provider
- ViewMigrationExtensions are convenience wrappers that call ViewSqlGenerator internally

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
