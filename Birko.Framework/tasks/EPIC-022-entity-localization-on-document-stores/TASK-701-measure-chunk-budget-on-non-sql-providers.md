---
id: TASK-701
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P3
assignee: ai
created: 2026-10-20
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Measure the localized filter chunk budget on the non-SQL providers

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). TASK-686 splits a broad match on a translated field into queries of at
most `LocalizedTranslationRows.ReadBatchSize` (500) GUIDs. That size was chosen for SQL Server's ~2 100-parameter limit.
On the other providers it is unmeasured. ElasticSearch's `terms` query defaults to 65 536 values, and Cosmos DB and
RavenDB have their own limits on `IN` and query size. 500 may be needlessly small, or it may be refused somewhere.

## Acceptance criteria

- [ ] Measure live: a translated-field filter matching more than 500 entities, counted, paged and deleted on MongoDB,
      ElasticSearch, RavenDB and Cosmos DB (emulator), as TASK-686 did on SQL.
- [ ] Only if a provider refuses 500, or a larger budget is measurably cheaper, add a per-provider budget at one
      producer (rule 52). Otherwise record the measurement and close.

## Out of scope

- SQL (TASK-686).

## Human test plan

N/A — measurement.

## Implementation plan

_Populated by `/tasks plan TASK-701` — leave empty until then._
