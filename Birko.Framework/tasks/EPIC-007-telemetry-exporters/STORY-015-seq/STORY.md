---
id: STORY-015
parent: EPIC-007
status: planned
created: 2026-05-28
---

# Seq log exporter

## User story

As an operator using Seq for structured logging, I want a Birko.Telemetry exporter that ships logs to Seq.

## Behaviour

- Implements the log exporter contract over `Seq.Client`
- Respects Birko's log enrichment (correlation id, tenant, etc.)
- Batched delivery with configurable flush interval
