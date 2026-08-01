---
id: TASK-020
feature: FEATURE-007
parent: STORY-015
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

# Implement Birko.Telemetry.Seq

## Context

Seq log exporter for `Birko.Telemetry`. Ships structured logs to Seq. Dependencies: `Birko.Telemetry`, `Seq.Client`.

## Acceptance criteria

- [ ] `Birko.Telemetry.Seq` shared project exists, registered everywhere
- [ ] Implements the log exporter contract
- [ ] Respects Birko log enrichment (correlation id, tenant, etc.)
- [ ] Batched delivery with configurable flush interval
- [ ] Settings descendant with Seq server URL + API key
- [ ] xUnit tests
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Seq Pro features beyond ingestion
