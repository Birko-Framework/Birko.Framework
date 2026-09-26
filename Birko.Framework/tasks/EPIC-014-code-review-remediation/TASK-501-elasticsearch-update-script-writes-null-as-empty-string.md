---
id: TASK-501
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
related: [TASK-498]
findings: [FIELD-014]
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.ElasticSearch]
---

# Elasticsearch's native `PropertyUpdate` writes a null Set as `""`

Found while working TASK-498 (planning pass), pre-existing, not introduced by it.

## Context

`Birko.Data.ElasticSearch/Stores/ElasticSearchStoreHelper.cs` `BuildUpdateScript` binds every parameter as
`scriptParams[paramName] = value ?? string.Empty`. So `new PropertyUpdate<T>().Set(x => x.Note, null)` through
`UpdateByQuery` stores `note: ""`, not null — a write that silently persists a different value than the caller
asked for (rule 22's family). On a non-string field (`int?`, `DateTime?`) the painless assignment stores a string
into a numeric/date field, which the mapping may reject at query time or coerce.

The substitution presumably exists because the script-params dictionary is `Dictionary<string, object>`
(non-nullable values). The fix is to allow a null parameter (NEST serialises it) or to emit
`ctx._source.f = null` without a parameter.

## Acceptance criteria

- [ ] A null Set through the native Elasticsearch update stores null, for a string and for a nullable value type.
- [ ] Shape test in `tests/Birko.Data.ElasticSearch.Tests/BuildUpdateScriptTests.cs` asserts no `""` substitution.
- [ ] Measured against a live server if one is available; otherwise say so, per the family's live-suite rule.

## Out of scope

- Increments — TASK-498 made a null delta impossible (`INumber<T>`, nullables refused).

## Implementation plan

_Populated by `/tasks plan TASK-501` — leave empty until then._
