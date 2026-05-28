---
id: TASK-024
parent: STORY-018
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: [TASK-016]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# AzureServiceBusHealthCheck

## Context

Health check for Azure Service Bus. Probes via the management REST API to confirm namespace reachable + entities present.

## Acceptance criteria

- [ ] `AzureServiceBusHealthCheck` lives in `Birko.Health.Azure`
- [ ] Probes namespace + optional queue/topic by name
- [ ] Supports managed-identity auth
- [ ] DI extension registers the check
- [ ] xUnit tests
- [ ] Recent Updates entry in `Birko.Health.Azure/CLAUDE.md`

## Out of scope

- Message-count thresholds (Telemetry concern)
