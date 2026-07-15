# Birko.Data.Sync.Xml.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.Sync.Xml` (CR-M163).

## Scope

- `AsyncXmlSyncKnowledgeStoreTests` — last-sync-time Set/Get round-trip + scope isolation + null/empty cases + `CreateKnowledgeItem` field derivation, against a real temp-file XML store. Also exercises the CR-M162 single-bulk-write fix (all matching items updated), and the CR-L225 dead-`Id` removal (reflection guard + a legacy file carrying `<Id>` still deserializes).

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, implicit usings, nullable). Imports the core projitems chain + `Birko.Data.XML` + `Birko.Data.Sync` + `Birko.Data.Sync.Xml`. File-based, no live server.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
