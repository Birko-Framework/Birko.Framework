# Birko.Data.Sync.CosmosDB.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.Sync.CosmosDB`.

## Scope

- `CosmosSyncTenantScopingTests` — offline model + conversion coverage: the CR-H100 tenant scoping
  (model carries `TenantId`, `FromInterface` stamps/leaves it), the CR-L211 shared
  `CosmosSyncKnowledgeItem.FromInterface` factory (full field copy, null-Guid populate per CR-M158,
  pass-through of an already-Cosmos item), and the dead-`InternalRecordId` removal guard (audit-gap
  extra alongside CR-L225; Cosmos analogue of RavenDB's CR-L219).

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, implicit usings, nullable). Imports the core
  projitems chain + `Birko.Data.CosmosDB` + `Birko.Data.Sync` + `Birko.Data.Sync.CosmosDB`.
- Offline only — the store's LINQ filter/query paths need a live Cosmos DB (emulator) and are
  integration-tier (see STORY-028).

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
