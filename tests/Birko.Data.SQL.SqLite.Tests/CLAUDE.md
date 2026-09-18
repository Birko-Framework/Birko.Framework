# Birko.Data.SQL.SqLite.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.SQL.SqLite` (the SQLite store backend plus its store factory / DI wiring).

## Project Location

`C:\Source\Birko.Data.SQL.SqLite.Tests\`

## Scope

- `SqLiteStoreFactoryTests` — verifies `SqLiteStoreFactory` + the `AddSqLiteStores` DI extension: eager creation of a missing database directory, relative-location resolution against `BaseDirectory`, rooted locations ignoring `BaseDirectory`, `GetConnector`/`GetStore` wiring against a real on-disk SQLite database, and DI registration of a resolvable singleton `ISqLiteStoreFactory`.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, implicit usings, nullable enabled, `IsTestProject`). Imports the `Birko.Helpers`, `Birko.Contracts`, `Birko.Configuration`, `Birko.Time`, `Birko.Serialization`, `Birko.Models.Contracts`, `Birko.Models`, `Birko.Models.SQL`, `Birko.Data.Core`, `Birko.Data.Patterns`, `Birko.Data.Stores`, `Birko.Data.Repositories`, `Birko.Data.ViewModel`, `Birko.Rules`, `Birko.Data.SQL`, `Birko.Data.SQL.View`, `Birko.Data.SQL.ViewModel`, and `Birko.Data.SQL.SqLite` `.projitems`; adds the `Microsoft.Data.Sqlite` and `Microsoft.Extensions.DependencyInjection` packages.
- One test class per source type; test both success and failure/guard paths.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
