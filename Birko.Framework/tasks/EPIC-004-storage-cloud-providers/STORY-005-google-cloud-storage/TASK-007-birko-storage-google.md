---
id: TASK-007
parent: STORY-005
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

# Implement Birko.Storage.Google

## Context

Google Cloud Storage implementation of `IFileStorage`. Dependencies: `Birko.Storage`, `Google.Cloud.Storage.V1`.

## Acceptance criteria

- [ ] `Birko.Storage.Google` shared project exists, registered everywhere
- [ ] `GoogleCloudStorage` implements `IFileStorage`
- [ ] Settings supports service-account JSON or workload-identity auth
- [ ] Lifecycle / versioning options surfaced via settings
- [ ] xUnit tests (mocked client or fake-gcs-server)
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- BigQuery export integration
- Pub/Sub notifications on bucket events
