---
id: TASK-501
parent: EPIC-014
feature: null
status: done
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

- [x] A null Set through the native Elasticsearch update stores null, for a string and for a nullable value type.
- [x] Shape test in `tests/Birko.Data.ElasticSearch.Tests/BuildUpdateScriptTests.cs` asserts no `""` substitution.
- [x] Measured against a live server if one is available; otherwise say so, per the family's live-suite rule.
      → ES 7.17.25. Red: `""` into the `long` and `date` fields was a 400 for the whole update (which before TASK-502
      was silently discarded, so the update was lost); green: `note`, `score` and `seen` store `null` (`_source` read).
- [x] `BuildUpdateScript` resolves the selector like `ApplyTo` and the MongoDB translator: a selector that is not a
      property throws `ArgumentException` (today a non-member selector throws `InvalidCastException` from a hard
      `(MemberExpression)` cast, and a public field is written as if it were a property). Grouped here because it is
      the same function; found by the `bulk-filter-operations` spec regen after TASK-498 (2026-09-26).

## Out of scope

- Increments — TASK-498 made a null delta impossible (`INumber<T>`, nullables refused).

## Implementation plan

1. Resolve the selector through `PropertyAssignment.PropertyInfo` and throw `ArgumentException` like the MongoDB
   translator. Take the field name from `ElasticSearch.StoredFieldName`, the producer for filters and sorts (TASK-613),
   so a member renamed by its mapping attribute is written where it is read. Recorded here because it came for free
   with the resolution.
2. A null value renders as `ctx._source.f = null` with no parameter. That avoids depending on how NEST serialises a
   null parameter.
3. Tests: shape tests (null, mapped name, refused selectors) and a live round trip checking the stored `_source`.

## Progress log

- 2026-10-15 — ES 7.17.25 (Docker). Red: three shape tests failed (`params.p_Name` with `""`, `owner` instead of
  `ownerRef`, `InvalidCastException`), and the live update was refused with 400. Green: Data.ElasticSearch 236,
  BackgroundJobs.ES 8, Workflow.ES 9, Sync.ES 12, Views 20.
