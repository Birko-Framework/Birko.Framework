---
id: TASK-654
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-17
depends-on: [TASK-641]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A health check reports stored grants whose role or resource type was retired

## Context

Spawned 2026-10-17 from [[TASK-641]]. That task made a stored grant whose role is no longer declared grant nothing,
instead of making every check of it throw, and moved the loud half into `StaleGrantDetector.FindAsync(store, table)`
(`Birko.Security.Grants/Services/StaleGrantDetector.cs`). That method returns the stale grants (`RoleNotDeclared` /
`ResourceTypeNotDeclared`); nothing calls it yet. The user's decision named a health check as the place it becomes
visible, so a role renamed in configuration shows up rather than silently removing access.

`Birko.Health.*` checks live in their own projects, one per dependency (`Birko.Health.Data.SQL` →
`SchemaDriftHealthCheck`, `Birko.Health.Data.IndexManagement` → `IndexDeclarationDriftHealthCheck`). A grants check
needs `Birko.Health` → `Birko.Security.Grants`, so it is a new sub-project, `Birko.Health.Security.Grants`, with the
registration and documentation the new-project checklist asks for (`/new-birko-subproject`).

## Acceptance criteria

- [ ] `StaleGrantHealthCheck` over `StaleGrantDetector.FindAsync`: **Degraded** (never Unhealthy, never Healthy) when
      it finds stale grants, with their count and descriptions in `Data`; Healthy when none; Degraded with the reason
      when the store cannot be read. Same contract as the index and schema drift checks.
- [ ] New project scaffolded and registered (`.slnx`, `.code-workspace`, aggregator `.csproj`), with README,
      CLAUDE.md, and entries in the README table, CLAUDE-projects.md (dependency graph too) and `docs/health.md`
- [ ] Tests: healthy, degraded with the stale grants named, the admin grant ignored, a failing store
- [ ] `Birko.Security.Grants/CLAUDE.md` and `docs/security.md` point at the check

## Out of scope

- Migrating or revoking stale grants automatically: the check reports, a person decides.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-654` — leave empty until then._
