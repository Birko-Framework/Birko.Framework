---
id: TASK-526
parent: STORY-059
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Web.Core: layered runtime configuration (defaults < server endpoint < local overrides)

## Context

DraCode's `DraCode.KoboldLair.Client/src/services/config.ts` (157 lines) builds its runtime settings from three layers:
built-in defaults, then `/api/config` from the server, then a local-storage override, with change notification. Only the
setting names are DraCode's.

Consumer side: DraCode `EPIC-018 / STORY-039` (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [ ] A typed runtime-config helper merges defaults, an optional server endpoint and an optional local override, and notifies on change
- [ ] Secrets are never written to local storage by the helper (tokens belong to the auth store)
- [ ] Tests for merge order and change notification

## Out of scope

- App-specific settings

## Human test plan

N/A — covered by unit tests.

## Implementation plan
