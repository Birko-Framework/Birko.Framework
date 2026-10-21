---
id: TASK-703
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: [TASK-701]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch refuses every filter on a translated field

## Context

Spawned 2026-10-21 from [[TASK-701]], measured live by its broad-match contract.

A filter on a translated field is rewritten by the localized wrappers to
`x.Guid != null && guids.Contains(x.Guid.Value)` (`LocalizedFilterResolution`, `LocalizedFilterHelper`). The
ElasticSearch translator accepted `guids.Contains(x.Guid)` but not the nullable `.Value` on the field side, so it threw
`NotSupportedException` ("could not be translated to an ElasticSearch query"). Every such filter failed on
ElasticSearch, small or chunked: the wrappers' filters on translated fields never worked there.

## Acceptance criteria

- [x] `x.Nullable.Value` on a direct member of the entity translates as that member's field (term and terms queries).
- [x] A filter on a translated field runs through the localized wrapper on a live ElasticSearch.
- [x] A `.Value` on something that is not the entity's own member keeps its current treatment.

## Out of scope

- Other expression shapes the translator refuses.

## Human test plan

N/A — asserted by the suite, live.

## Implementation plan

1. `ParseMember` unwraps a nullable `.Value` on a direct member of the parameter, as it already does `.HasValue`.
2. Offline translation tests and a live localized-filter test.

## Outcome

Done 2026-10-21. `ParseMember` treats `x.Nullable.Value` on a direct member as that member's field. A closure's
`.Value` is still evaluated as a value, and a nested member is still refused.

**Tests:** `Task703NullableValueTranslationTests` (3: two offline translations, one live filter on a translated field).

**Mutation:** the fix reverted, all 3 red.

**Regression:** Birko.Data.ElasticSearch.Tests 305/305 live, and the seven other suites importing Birko.Data.ElasticSearch.
