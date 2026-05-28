---
id: EPIC-007
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Telemetry.Prometheus, Birko.Telemetry.Seq, Birko.Telemetry.Grafana]
---

# Birko.Telemetry — Additional exporters

## Area of concern

Expand `Birko.Telemetry` beyond the existing OTLP exporter — add Prometheus (metrics scraping), Seq (structured log shipping), and Grafana LGTM stack (Loki/Tempo/Mimir with optional dashboard provisioning).

## Success criteria

- Three sibling shared projects exist and are registered
- Each hooks into `Birko.Telemetry`'s metric / log / trace pipeline via DI extensions
- Grafana exporter optionally provisions dashboards via the Grafana HTTP API
