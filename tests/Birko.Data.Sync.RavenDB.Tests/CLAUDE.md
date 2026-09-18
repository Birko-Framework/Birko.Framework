# Birko.Data.Sync.RavenDB.Tests

## Overview
Unit tests for Birko.Data.Sync.RavenDB — the RavenDB-backed sync-knowledge store.

## Project Location
`C:\Source\Birko\Framework	ests\Birko.Data.Sync.RavenDB.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **Compile guard + model tests.** Building against the shared projitems compile-verifies the store
  fixes: CR-C19 (`RavenSyncKnowledgeItem` implements the canonical `ITenant`; tenant queries filter
  on `ITenant.TenantGuid` instead of a StartsWith on the record's random Guid, and
  `ConvertToRavenItem` copies the tenant from any incoming `ITenant` item so the write path
  populates it) and CR-C20 (async `DeleteKnowledgeAsync` deletes the tracked entity, not a `Guid?`).
- **No live server.** The Raven query/delete paths need a running RavenDB instance and are not
  exercised. Only the model (`ITenant` conformance, `TenantGuid`/`TenantName`, `GenerateDocumentId`)
  is tested offline.
