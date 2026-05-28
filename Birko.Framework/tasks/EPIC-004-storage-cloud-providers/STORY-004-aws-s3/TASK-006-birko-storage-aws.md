---
id: TASK-006
parent: STORY-004
status: todo
priority: P1
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Storage.Aws

## Context

AWS S3 implementation of `IFileStorage`. Dependencies: `Birko.Storage`, `AWSSDK.S3`.

## Acceptance criteria

- [ ] `Birko.Storage.Aws` shared project exists, registered everywhere
- [ ] `AwsS3Storage` implements `IFileStorage` (read / write / exists / delete / list, streaming)
- [ ] Settings descendant of `RemoteSettings` (bucket, region, credentials)
- [ ] Presigned URL generation
- [ ] Object metadata + tags support
- [ ] xUnit tests (mocked AWSSDK or LocalStack)
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- S3 Select (analytical queries)
- Cross-region replication setup
