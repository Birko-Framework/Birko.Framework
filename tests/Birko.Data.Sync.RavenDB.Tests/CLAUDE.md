# Birko.Data.Sync.RavenDB.Tests

## Overview
Unit tests for Birko.Data.Sync.RavenDB — the RavenDB-backed sync-knowledge store.

## Project Location
`C:\Source\Birko\Framework.Tests\Birko.Data.Sync.RavenDB.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **Compile guard + model tests.** Building against the shared projitems compile-verifies the store
  fixes: CR-C19 (tenant queries now filter on `RavenSyncKnowledgeItem.TenantGuid` instead of a
  StartsWith on the record's random Guid) and CR-C20 (async `DeleteKnowledgeAsync` deletes the
  tracked entity, not a `Guid?`).
- **No live server.** The Raven query/delete paths need a running RavenDB instance and are not
  exercised. Only the model (`TenantGuid`, `GenerateDocumentId`) is tested offline.
- Populating `TenantGuid` on the write path (threading `tenantId` through the update methods) is
  tracked follow-up work for CR-C19.
