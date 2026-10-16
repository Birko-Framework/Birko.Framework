---
id: TASK-644
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch filter: a field compared with an untranslatable operand becomes an exists test

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `filter-expression-translation`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

In `ParseComparison`'s null branch (`Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:316`), when one operand is
a field and the other cannot be translated (`x.Name == x.Sub.Name`, `x.Code == x.Name.Substring(1)`), the
field-vs-field refusal does not fire: the value is null, and the comparison becomes `MustNot(exists field)` or `exists`.
A silent wrong answer that also bypasses the nested-member refusal (rules 42, 44). The spec records it as current
behaviour.

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Such a comparison is refused with NotSupportedException
- [ ] Red on the current code; the spec `docs/specs/filter-expression-translation.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-644` — leave empty until then._
