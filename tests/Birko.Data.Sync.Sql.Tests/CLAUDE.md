# Birko.Data.Sync.Sql.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.Sync.Sql` (CR-M166), run against a real on-disk SQLite connector.

## Scope

- `AsyncSqlSyncKnowledgeStoreTests` — GetLastSyncTime Max/null-on-empty, SetLastSyncTime updating all matching items + scope isolation + null, CreateKnowledgeItem deletion-flag derivation. The model is attribute-mapped, so no ModelMapRegistry is needed.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`). Imports the core chain + `Birko.Data.SQL` + `Birko.Data.SQL.SqLite` + `Birko.Data.Sync` + `Birko.Data.Sync.Sql`; adds `Microsoft.Data.Sqlite`.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
