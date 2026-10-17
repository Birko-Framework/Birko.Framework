---
id: TASK-654
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] `StaleGrantHealthCheck` over `StaleGrantDetector.FindAsync`: **Degraded** (never Unhealthy, never Healthy) when
      it finds stale grants, with their count and descriptions in `Data`; Healthy when none; Degraded with the reason
      when the store cannot be read. Same contract as the index and schema drift checks.
- [x] New project scaffolded and registered (`.slnx`, `.code-workspace`, aggregator `.csproj`), with README,
      CLAUDE.md, and entries in the README table, CLAUDE-projects.md (dependency graph too) and `docs/health.md`
- [x] Tests: healthy, degraded with the stale grants named, the admin grant ignored, a failing store
- [x] `Birko.Security.Grants/CLAUDE.md` and `docs/security.md` point at the check

## Out of scope

- Migrating or revoking stale grants automatically: the check reports, a person decides.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-654` — leave empty until then._

## Resolution (2026-10-17)

**New sub-project `Birko.Health.Security.Grants`**, scaffolded with `/new-birko-subproject`:
- `.shproj` and `.projitems` with matching hex GUID `1f0ed27b-b41d-46ea-b144-3f92b3fd6d75`, README and CLAUDE.md.
- Registered in `Birko.Framework.slnx` (Health and Tests) and `Birko.Framework.code-workspace` (sorted). The monorepo
  has no aggregator `.csproj`; CI builds the `.slnx`.
- Listed in the README tables, `CLAUDE-projects.md` (dependency graph, catalog, test list) and `docs/health.md`
  (section and table). The doc-index sweep finds it.
- No spec area: `Birko.Health*` is deliberately outside `docs/specs/.map.yml`.

**`StaleGrantHealthCheck(IResourceGrantStore, GrantRoleTable)`** over `StaleGrantDetector.FindAsync`:
- **Healthy** with `stale = 0`.
- **Degraded** when grants are stale. `Data` carries `stale` (the full count), `roleNotDeclared`,
  `resourceTypeNotDeclared` and `staleGrants` (descriptions, capped at `MaxListed` = 50), and the description names
  the undeclared roles.
- **Degraded** with the exception when the store cannot be read.
- A cancellation the caller asked for propagates. Never Unhealthy.
- The admin grant is ignored, which the detector already does and the test pins.
- **Deliberate difference:** the index-drift check answers a thrown probe with Unhealthy. The acceptance criteria ask
  for Degraded here, and that is what this check does.

**Tests:** `tests/Birko.Health.Security.Grants.Tests` (5, offline):
- healthy, with the admin grant ignored;
- degraded, naming both reasons;
- a failing store;
- the listing capped while the count is not;
- null arguments refused.

The test project is registered in the `.slnx`, so CI's `tests/*/` loop runs it.

**Pointers:** `Birko.Security.Grants/CLAUDE.md` and `docs/security.md` point at the check.
