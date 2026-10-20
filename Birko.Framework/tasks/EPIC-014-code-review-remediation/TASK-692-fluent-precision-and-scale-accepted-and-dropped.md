---
id: TASK-692
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

# Fluent `HasPrecision` / `HasScale` are accepted and dropped

## Context

Found while planning [[TASK-684]] / [[TASK-691]] (2026-10-20). `ModelMapRegistry.ApplyToDatabase` documents that
`HasPrecision` and `HasScale` are not applied. A fluent decimal mapping therefore silently gets the provider's default
precision, while `[PrecisionField]` / `[ScaleField]` would be honoured. That is rule 55: a builder method that accepts a
declaration and does nothing.

About 50 string columns in Users, Customers, Product, Pricing and Inventory call `HasPrecision(n)`, apparently meaning a
length. On the attribute path `[PrecisionField(n)]` on a string *is* a length (`CharField`), so honouring it on the
fluent path would change those columns. Measure what those mappings render today before deciding.

TASK-691 adds the registration channel (`DataBase.RegisterMaxLength`) that this fix would extend.

## Acceptance criteria

- [ ] Decide and record what fluent `HasPrecision` / `HasScale` mean on decimals and on strings: honoured,
      refused, or a string's `HasPrecision` mapped to a length. The ~50 existing string usages are measured first.
- [ ] Whatever is accepted is honoured by every SQL provider's DDL; what is not is refused (rule 55).
- [ ] Tests in Birko.Models.SQL.Tests, plus a catalogue read-back on one live provider (rule 63).

## Out of scope

- `HasMaxLength` itself: [[TASK-691]].

## Human test plan

N/A — library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-692` — leave empty until then._
