---
id: TASK-028
feature: FEATURE-010
parent: EPIC-010
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

# Attribute-driven RavenDB index definitions (Option B)

## Context

Final remaining RavenDB index management enhancement. `RavenDBIndexManager` already supports IndexDefinition + AbstractIndexCreationTask, plus `DeployFromAssemblyAsync` and Map/Reduce query helpers. Attribute-driven discovery (Option B from the original design) was deferred.

## Acceptance criteria

- [ ] `[RavenIndex]` (or similar) attributes for marking model properties as indexed
- [ ] Discovery walks types in a target assembly, builds `IndexDefinition`s from attribute metadata
- [ ] Idempotent — running twice doesn't redeploy unchanged indexes
- [ ] Compatible with existing `IndexDefinition` / `AbstractIndexCreationTask` paths
- [ ] xUnit tests covering discovery + deployment + idempotency

## Out of scope

- Index lifecycle UI / admin tooling
