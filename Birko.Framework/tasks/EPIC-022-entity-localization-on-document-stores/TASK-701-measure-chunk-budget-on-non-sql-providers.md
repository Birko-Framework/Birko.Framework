---
id: TASK-701
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Measure live: a translated-field filter matching more than 500 entities, counted, paged and deleted on MongoDB,
      ElasticSearch, RavenDB and Cosmos DB (emulator), as TASK-686 did on SQL.
- [x] Only if a provider refuses 500, or a larger budget is measurably cheaper, add a per-provider budget at one
      producer (rule 52). Otherwise record the measurement and close.

## Out of scope

- SQL (TASK-686).

## Human test plan

N/A — measurement.

## Implementation plan

1. A shared contract, `tests/Shared/LocalizationContract/BroadMatchContract.cs`, linked into the MongoDB, ElasticSearch,
   RavenDB and Cosmos DB suites: TASK-686's 2 200-entity match counted, paged and deleted through the localized wrapper,
   and probes of one query over 500, 2 200 and 10 000 GUIDs in the shape the wrapper renders.
2. Change the budget only on a refusal or a measurable saving (rule 52).

## Outcome

Done 2026-10-21. **The budget stays 500 on every provider**, recorded rather than changed (rule 52): no provider refuses
it, and a larger one would save a few short queries in operations their other work dominates.

**Measured live (budget 500, a 2 200-entity match in 3 300; one query of N GUIDs):**

| Provider | count / page / delete | 500 | 2 200 | 10 000 |
|---|---|---|---|---|
| MongoDB 7 | 267 / 332 / 366 ms | 4 ms | 10 ms | 58 ms |
| ElasticSearch | 744 / 1 027 / 1 171 ms | 49 ms | 10 ms | 70 ms |
| RavenDB 7.2 | 1 987 / 1 376 / 1 653 ms | 181 ms | 133 ms | 258 ms |
| Cosmos DB (vnext emulator) | 1 259 / 1 637 / 2 553 ms | 57 ms | 51 ms | 179 ms |

(The single-query columns are counts; reads were alike.) Every provider accepted 10 000 GUIDs in one query, so 500 is
far from any limit. A chunk query costs 4–181 ms against whole operations of 0.3–2.6 s, which the translation reads and
the in-memory paging of the whole match dominate. On real Cosmos DB the query text is capped at 512 KB: about 10 000
GUIDs, so 500 stays well inside it there too (the emulator does not enforce it).

**Found on the way:** [[TASK-703]]. On ElasticSearch the broad match threw: every filter on a translated field did,
since the translator refused `x.Guid.Value`. Fixed and committed separately (29072e4d) before this measurement.

**Tests:** `BroadMatchContract` (4: the broad match and the three probe sizes) on MongoDB, ElasticSearch, RavenDB and
Cosmos DB, all green live.
