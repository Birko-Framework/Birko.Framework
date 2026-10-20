---
id: TASK-696
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-689]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Regenerate the 14 spec areas TASK-689 extended, including the new backend-store-implementations

## Context

Filed at TASK-689's close (2026-10-20, decided by the user). The first coverage check of `docs/specs/.map.yml` moved
279 files into areas (`coverage-drift:`) and added one area. No spec was regenerated, so these read stale on
`/specs verify` until harvested:

- `backend-store-implementations` (new; no spec yet): every backend's `Stores/` and `Repositories/`, specced side by
  side to show where they diverge from the store contract.
- `store-crud-contract`, `schema-index-and-ddl` (Models.SQL, the connectors' DDL / drift / repair),
  `views-and-aggregation` (the whole of `Birko.Data.SQL.View`), `llm-provider-and-agents` (AI.Providers, Agents,
  Orchestration, Resilience), `store-decorator-composition` (EventSourcing, Telemetry's wrappers),
  `security-and-authorization` (Grants.AspNetCore, the transport auth adapters, `OwnerAccess`), `core-model-contracts`,
  `unit-of-work-and-transactions`, `filter-expression-translation`, `settings-configuration-chain`, `tenant-isolation`,
  `event-bus-and-messaging`, `migrations`.

The spec diffs are the point. These files changed for months with no spec review, so a diff showing behaviour nobody
intended is a finding (CLAUDE.md, lifecycle step 9), not churn.

## Acceptance criteria

- [ ] `/specs regen` over the 14 areas (batched), each diff reviewed; unexplained behaviour raised as tasks.
- [ ] `backend-store-implementations.md` written, with a scenario per backend divergence it finds.
- [ ] `/specs verify` reports the 14 areas fresh.

## Out of scope

- The own-tree families: [[TASK-226]].

## Human test plan

N/A — documentation, reviewed as diffs.

## Implementation plan

_Populated by `/tasks plan TASK-696` — leave empty until then._
