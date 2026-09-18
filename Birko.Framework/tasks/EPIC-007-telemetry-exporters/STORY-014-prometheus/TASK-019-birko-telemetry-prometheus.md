---
id: TASK-019
feature: FEATURE-007
parent: STORY-014
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

# Implement Birko.Telemetry.Prometheus

## Context

Prometheus metrics exporter for `Birko.Telemetry`. Exposes `/metrics` in Prometheus text format. Dependencies: `Birko.Telemetry`, `Prometheus.Client`.

## Acceptance criteria

- [ ] `Birko.Telemetry.Prometheus` shared project exists, registered everywhere
- [ ] Translates Birko metric types → Prometheus counter / gauge / histogram / summary
- [ ] `/metrics` endpoint registered via DI extension
- [ ] Settings supports custom endpoint path + label prefixes
- [ ] xUnit tests
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- Push gateway support (could be a follow-up)
