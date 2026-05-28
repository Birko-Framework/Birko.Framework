---
id: STORY-016
parent: EPIC-007
status: planned
created: 2026-05-28
---

# Grafana LGTM stack exporter

## User story

As an operator running the Grafana LGTM stack (Loki / Grafana / Tempo / Mimir), I want a unified exporter that hits the right component per signal type.

## Behaviour

- Logs → Loki
- Traces → Tempo
- Metrics → Mimir
- Optional dashboard provisioning via the Grafana HTTP API
- Single settings class with per-component endpoint overrides
