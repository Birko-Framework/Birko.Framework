---
id: TASK-022
feature: FEATURE-008
parent: STORY-017
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: [TASK-014]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# RabbitMqHealthCheck

## Context

Health check for RabbitMQ brokers. Probes via the management HTTP API so it reports a healthy/unhealthy result without holding an AMQP connection.

## Acceptance criteria

- [ ] `RabbitMqHealthCheck` lives in `Birko.Health.Data`
- [ ] Probes management HTTP API: auth, vhost reachable
- [ ] Optional queue-depth check
- [ ] DI extension registers the check
- [ ] xUnit tests with Testcontainers RabbitMQ
- [ ] Recent Updates entry in `Birko.Health.Data/CLAUDE.md`

## Out of scope

- Channel-level metrics (better as Telemetry exporter)
