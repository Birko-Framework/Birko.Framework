---
id: EPIC-004
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Storage.Aws, Birko.Storage.Google, Birko.Storage.Minio]
---

# Birko.Storage — Cloud providers

## Area of concern

Extend `Birko.Storage` with AWS S3, Google Cloud Storage, and MinIO (S3-compatible) implementations of `IFileStorage`. Pairs with the existing `Birko.Storage.AzureBlob` and `Birko.Storage` (local file system).

## Success criteria

- Three sibling shared projects exist and are registered everywhere
- Each implements `IFileStorage` (read/write/exists/delete/list, streaming, metadata)
- xUnit tests against SDK mocks or a local LocalStack/MinIO instance
