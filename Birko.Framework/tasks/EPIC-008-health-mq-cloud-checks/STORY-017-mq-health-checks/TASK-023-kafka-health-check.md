---
id: TASK-023
parent: STORY-017
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: [TASK-015]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# KafkaHealthCheck

## Context

Health check for Apache Kafka clusters. Probes via a lightweight metadata request — confirms cluster reachable + reports broker count.

## Acceptance criteria

- [ ] `KafkaHealthCheck` lives in `Birko.Health.Data`
- [ ] Uses `Confluent.Kafka` `AdminClient.GetMetadata`
- [ ] Optional partition-health check (ISR < replicas → degraded)
- [ ] DI extension registers the check
- [ ] xUnit tests with Testcontainers Kafka
- [ ] Recent Updates entry in `Birko.Health.Data/CLAUDE.md`

## Out of scope

- Consumer-lag metrics (Telemetry exporter)
