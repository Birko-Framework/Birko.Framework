---
id: STORY-004
parent: EPIC-004
status: planned
created: 2026-05-28
---

# AWS S3 storage

## User story

As a developer, I want a Birko.Storage AWS S3 provider so I can store files in S3 buckets through the `IFileStorage` abstraction.

## Behaviour

- Implements `IFileStorage` (read / write / exists / delete / list, streaming)
- Settings descendant of `RemoteSettings` (bucket, region, credentials)
- Presigned URL generation for signed downloads
- Object metadata + tags support
