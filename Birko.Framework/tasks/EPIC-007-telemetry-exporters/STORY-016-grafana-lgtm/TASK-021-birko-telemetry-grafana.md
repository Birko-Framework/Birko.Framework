---
id: TASK-021
parent: STORY-016
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

# Implement Birko.Telemetry.Grafana

## Context

Grafana LGTM stack exporter — logs → Loki, traces → Tempo, metrics → Mimir. Optional dashboard provisioning via Grafana HTTP API.

## Acceptance criteria

- [ ] `Birko.Telemetry.Grafana` shared project exists, registered everywhere
- [ ] Log exporter pushes to Loki
- [ ] Trace exporter pushes to Tempo (OTLP path is fine)
- [ ] Metric exporter pushes to Mimir (Prometheus remote_write)
- [ ] Optional dashboard provisioning hooks via Grafana HTTP API
- [ ] Single settings class with per-component endpoint overrides
- [ ] xUnit tests
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Grafana alerting rule provisioning (could be a follow-up)
