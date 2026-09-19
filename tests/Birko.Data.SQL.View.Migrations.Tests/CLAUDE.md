# Birko.Data.SQL.View.Migrations.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.SQL.View.Migrations` — the `ViewMigrationExtensions`
create/drop-view migration helpers (CR-M150).

## Project Location

`tests/Birko.Data.SQL.View.Migrations.Tests/`

## Scope

- `ViewMigrationExtensionsTests` — exercises `CreateView` / `CreateViewAsync` / `DropView` / `DropViewAsync`
  (by-type and by-name, sync + async) through a **recording fake `DbConnection`** wrapped in a real
  `SqlMigrationContext`, asserting the generated DDL (`CREATE OR REPLACE VIEW …` / `DROP VIEW IF EXISTS …`),
  the custom quote-char, and that the context's `DbTransaction` is propagated onto the command. Also covers
  the wrong-context-type guard (`InvalidOperationException` for a non-`SqlMigrationContext`) and the argument
  guards (null context, null view type, empty view name).

A recording fake — not a live SQLite connection — is used because `ViewSqlGenerator` emits
`CREATE OR REPLACE VIEW`, which SQLite does not accept; the fake asserts DDL + transaction wiring
independent of provider dialect (the finding's sanctioned "at minimum" approach). Base tables in the
decorated view type are resolved from `[Table]` attributes, so no `ModelMapRegistry` setup is needed.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, implicit usings, nullable enabled, `IsTestProject`). Imports
  the `Birko.Data.SQL.View.Migrations` `.projitems` (which compiles `ViewSqlGenerator` + `ViewMigrationExtensions`)
  plus its dependency chain (`Birko.Data.SQL`, `Birko.Data.SQL.View`, `Birko.Data.Migrations`,
  `Birko.Data.Migrations.SQL`, and the shared core/model projitems); adds the `Microsoft.Data.Sqlite` and
  `Microsoft.Extensions.DependencyInjection` packages.
- One test class per source type; test both success and failure/guard paths.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
