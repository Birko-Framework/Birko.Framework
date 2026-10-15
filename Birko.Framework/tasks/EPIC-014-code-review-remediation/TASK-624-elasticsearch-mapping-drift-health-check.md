---
id: TASK-624
parent: EPIC-014
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-15
depends-on: [TASK-621]
blocks: []
related: [TASK-621, TASK-269]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Elasticsearch mapping drift is reported on demand only: wire it into a health check

## Context

TASK-621 added `DetectMappingDriftAsync()` / `DetectMappingDrift()` to the Elasticsearch stores, but nothing calls them
on its own. The SQL side has `SchemaDriftHealthCheck` in its own project (`Birko.Health.Data.SQL`), because
`Birko.Health.Data` is promised zero Birko dependencies (CLAUDE-projects.md § Dependency Flow). An Elasticsearch drift
check needs `Birko.Data.ElasticSearch`, so it needs a project of its own, which means the full new-project checklist.

## Acceptance criteria

- [x] `Birko.Health.Data.ElasticSearch` (scaffolded with `/new-birko-subproject`: slnx, workspace, csproj, docs index)
      with `ElasticSearchMappingDriftHealthCheck` over a set of stores: **Degraded, never Unhealthy**; "could not
      determine" (unsupported, index missing for every store) never Healthy. Mirror `SchemaDriftHealthCheck`'s data keys.
      → Scaffolded with `/new-birko-subproject`: `.shproj` + `.projitems` (matching hex GUID), CLAUDE.md, README.md,
      registered in the `.slnx`, the workspace, README, CLAUDE-projects (catalog, dependency flow, tests) and
      docs/health.md. The skill's `Birko.Framework.csproj` aggregator no longer exists; it was skipped. The check
      takes one probe per store, because ES stores are generic per entity. Degraded on drift or unsupported, Healthy
      with "not checked" for an index not created yet, Unhealthy only when a probe throws.
- [x] Live tests on ES 7.17.25: Healthy for a clean store, Degraded naming the drifted field, and the
      "could not determine" case.
      → Five offline tests, one per answer a probe can give, and one live test (a real store over an index mapped for
      an older model is Degraded). 6/6.
- [x] `docs/health.md` documents it beside the SQL check.
      → A section beside the SQL check, plus the table row.

## Out of scope

- The comparison itself (TASK-621).

## Human test plan

N/A — covered by the live tests.

## Implementation plan

_Drafted when picked._
