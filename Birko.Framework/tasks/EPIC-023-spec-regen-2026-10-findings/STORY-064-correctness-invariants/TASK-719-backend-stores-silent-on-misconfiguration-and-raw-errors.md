---
id: TASK-719
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-14]
pr: null
github-issue: null
jira-key: null
---

# Backend stores: silent no-ops when unconfigured, raw driver errors, delegate before Guid

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. Unconfigured MongoDB, RavenDB, Cosmos DB and InfluxDB stores do nothing silently and their update-miss does not throw; MongoDB counts an unacknowledged write as a hit. Cosmos delete of a missing item throws a raw 404 `CosmosException`. Cosmos and RavenDB `SaveAsync` run the store delegate before `EnsureGuid`; a native upsert collision surfaces as the raw driver exception, not `StoreUniqueViolationException`. Cosmos batch bulk update skips Guid-less items silently.

Evidence: AsyncMongoDBStore.cs:226,251; AsyncCosmosDBStore.cs:333,436-438,560; AsyncRavenDBStore.cs (SaveAsync).

## Acceptance criteria

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-719` — leave empty until then._
