---
id: TASK-025
parent: STORY-018
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: [TASK-017]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# AwsSqsHealthCheck

## Context

Health check for AWS SQS. Probes via `GetQueueAttributes` (cheapest valid call). Lives in a new `Birko.Health.Aws` sibling project scaffolded as part of this task.

## Acceptance criteria

- [ ] `Birko.Health.Aws` shared project exists, registered everywhere
- [ ] `AwsSqsHealthCheck` implementation
- [ ] Probes a configured queue URL via `GetQueueAttributes`
- [ ] Supports IAM role + access-key auth
- [ ] DI extension registers the check
- [ ] xUnit tests against LocalStack
- [ ] CLAUDE.md / README.md / License.md / .gitignore for the new project

## Out of scope

- SQS message-age / queue-depth alarms (Telemetry concern)
