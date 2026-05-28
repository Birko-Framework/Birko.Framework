---
id: STORY-005
parent: EPIC-004
status: planned
created: 2026-05-28
---

# Google Cloud Storage

## User story

As a developer on GCP, I want a Birko.Storage GCS provider so I can store files in Google Cloud buckets through `IFileStorage`.

## Behaviour

- Implements `IFileStorage` over `Google.Cloud.Storage.V1`
- Service-account JSON or workload-identity auth
- Lifecycle / versioning options surfaced via settings
