---
id: STORY-014
parent: EPIC-007
status: planned
created: 2026-05-28
---

# Prometheus exporter

## User story

As an operator scraping metrics with Prometheus, I want a Birko.Telemetry exporter that exposes `/metrics` in Prometheus format.

## Behaviour

- Hooks into `Birko.Telemetry`'s metric pipeline
- Exposes the standard `/metrics` endpoint via `Prometheus.Client`
- Translates Birko metric types to Prometheus counter/gauge/histogram/summary
