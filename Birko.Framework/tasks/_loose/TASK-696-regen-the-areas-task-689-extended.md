---
id: TASK-696
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] `/specs regen` over the 14 areas (batched), each diff reviewed; unexplained behaviour raised as tasks.
- [x] `backend-store-implementations.md` written, with a scenario per backend divergence it finds.
- [x] `/specs verify` reports the 14 areas fresh.

## Out of scope

- The own-tree families: [[TASK-226]].

## Human test plan

N/A — documentation, reviewed as diffs.

## Implementation plan

1. One evidence pass for `shaped-by` over all areas; one harvest agent per area writing the body to the scratchpad and
   reporting its behavioural changes and suspected bugs; diffs reviewed with the user; frontmatter stamped at HEAD.
2. Also `entity-localization`, which EPIC-022 changed after TASK-689.

## Outcome

Done 2026-10-21. 15 areas regenerated at `50828c68`: the 14 listed plus `entity-localization`. `backend-store-implementations`
is new (21 requirements, one scenario per backend divergence). The map gained six files added this session (decided by the
user): `AmbientSession`, `RetryableWrite`, `SqlRetryableWrite`, `SqlSavepoint` → unit-of-work-and-transactions;
`IRealtimeReadStore` → store-crud-contract; `ISoftDeleteStoreWrapper` → store-decorator-composition. Unmapped files: 0 of 5 007.

**Diff review:** every changed or removed requirement traces to a closed task (TASK-209, 160/173/660, 641, 692/693/695,
214/219, 259/279/332/656, 668, 697–705); several predate a stale old spec. The rest is new coverage. Confirmed intended by
the user. `shaped-by`: FEATURE-014 newly on backend-store-implementations and security-and-authorization, FEATURE-006
kept on llm-provider-and-agents; 376 feature-linked tasks, 259 unresolved, 1 subject-led commit the state gate refuses.

**Found and fixed during the run:**
- [[TASK-704]] (1d877b67): ElasticSearch read a bare `x.Flag.Value` on a `bool?` as a value-less term — a regression from
  TASK-703, found by the `filter-expression-translation` harvest.
- [[TASK-705]] (50828c68): a savepoint lost what an entity-not-found collection write stored, and a failed rollback to it
  left the caller's unit committable — found by the `entity-localization` harvest.

**Suspected bugs:** 23 groups, filed as a review-intake epic (decided by the user).
