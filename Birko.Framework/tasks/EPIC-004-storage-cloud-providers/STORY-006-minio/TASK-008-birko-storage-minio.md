---
id: TASK-008
parent: STORY-006
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Storage.Minio

## Context

MinIO (S3-compatible) implementation of `IFileStorage`. Useful for on-prem object storage. Dependencies: `Birko.Storage`, MinIO .NET SDK.

## Acceptance criteria

- [ ] `Birko.Storage.Minio` shared project exists, registered everywhere
- [ ] `MinioStorage` implements `IFileStorage`
- [ ] Settings supports custom endpoint, region, path-style addressing
- [ ] Presigned URLs work
- [ ] xUnit tests against a local MinIO instance
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- MinIO admin API (bucket lifecycle policies, user management)
