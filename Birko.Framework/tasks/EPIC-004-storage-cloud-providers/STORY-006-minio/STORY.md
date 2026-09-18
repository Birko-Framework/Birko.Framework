---
id: STORY-006
parent: EPIC-004
status: planned
created: 2026-05-28
---

# MinIO (S3-compatible)

## User story

As a developer running on-prem object storage, I want a MinIO provider so the same `IFileStorage` consumers work against MinIO buckets without modification.

## Behaviour

- Implements `IFileStorage` over the MinIO .NET SDK
- Configurable endpoint, region, and path-style addressing
- Reuses S3 semantics where possible (presigned URLs, tags, metadata)
