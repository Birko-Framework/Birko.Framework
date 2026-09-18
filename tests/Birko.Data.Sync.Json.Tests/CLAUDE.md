# Birko.Data.Sync.Json.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.Sync.Json` (CR-M163).

## Scope

- `AsyncJsonSyncKnowledgeStoreTests` — last-sync-time Set/Get round-trip + scope isolation + null/empty cases + `CreateKnowledgeItem` field derivation, against a real temp-file JSON store. Also exercises the CR-M162 single-bulk-write fix (all matching items updated).

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, implicit usings, nullable). Imports the core projitems chain + `Birko.Data.JSON` + `Birko.Data.Sync` + `Birko.Data.Sync.Json`. File-based, no live server.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
