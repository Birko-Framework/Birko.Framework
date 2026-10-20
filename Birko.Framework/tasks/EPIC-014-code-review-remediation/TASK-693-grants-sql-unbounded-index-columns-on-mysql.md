---
id: TASK-693
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-691]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Security.Grants.SQL's indexes over unbounded string columns — unmeasured on MySQL

## Context

Found while planning [[TASK-684]] (2026-10-20), not measured. `Birko.Security.Grants.SQL` declares index-manager
indexes over string columns such as `ResourceType` and `Email`, which have no declared length. Rule 37 renders an
index-key string as `VARCHAR(255)` on MySQL. A composite key of several such columns can exceed InnoDB's 3 072 bytes
under utf8mb4, as the translation key did (TASK-680: 3 204 bytes, ERROR 1071). Schema-ensure would then report the
index as failed and leave it absent.

With [[TASK-691]] a fluent `HasMaxLength` can bound those columns.

## Acceptance criteria

- [ ] Measure on live MySQL 8.4 which Grants.SQL indexes are created and which are reported in
      `IndexCreationFailures` (rule 54: measure before acting).
- [ ] For any index that fails, bound its columns (`HasMaxLength`) so it fits, and assert it from the catalogue
      (rule 63). If none fails, record the measurement and close.

## Out of scope

- The translation table: [[TASK-684]].

## Human test plan

N/A — asserted by the live MySQL suite.

## Implementation plan

_Populated by `/tasks plan TASK-693` — leave empty until then._
