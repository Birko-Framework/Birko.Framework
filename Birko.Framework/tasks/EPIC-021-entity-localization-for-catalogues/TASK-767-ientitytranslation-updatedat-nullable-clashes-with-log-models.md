---
id: TASK-767
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-23
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `IEntityTranslation.UpdatedAt` is `DateTime?`, so a translation model cannot derive from a log model

## Context

Found 2026-10-23 while planning Symbio TASK-945, the first consumer of EPIC-021. The Symbio side is blocked on this.

TASK-680 made the translation model consumer-supplied (`TTranslation : AbstractModel, IEntityTranslation`). The interface
declares `DateTime? UpdatedAt` (`Birko.Data.Localization/Models/IEntityTranslation.cs:28`). The SQL log model declares
`public override DateTime UpdatedAt` (`Birko.Data.SQL/Models/AbstractLogModel.cs:18`). Every consumer entity built on a log
model therefore cannot implement the interface. In Symbio that is every entity, because `BaseEntity` → `TenantEntity` sit on it.

The workarounds both fail:
- **Hiding the property with `new`** makes `Type.GetProperty("UpdatedAt")` ambiguous inside
  `LocalizedTranslationRows.RequireQueryableModel`.
- **Implementing the interface explicitly** is refused by that same check.

The framework's own test model (`tests/Birko.Data.Localization.SQL.Tests/TranslationTableTests.cs:30`) derives from a bare
`AbstractModel`, so this combination was never exercised.

## Acceptance criteria

- [ ] Choose and record here: `UpdatedAt` becomes `DateTime` on the interface; or it leaves the interface and the model owns
      its timestamp (the decorator stamps through `ITimestamped` when present); or the check accepts both.
- [ ] A translation model deriving from the SQL `AbstractLogModel` (i.e. `DateTime UpdatedAt`) works through
      `EntityTranslations` end to end on SQLite: set, read back, remove. `RequireQueryableModel` accepts it.
- [ ] `EntityTranslationModel` and the existing tests keep working. Mutation-proven: the new test fails against today's
      interface.
- [ ] Docs of `Birko.Data.Localization` say which base models a translation model may derive from.

## Out of scope

- Soft delete on the translation chain stays refused (`RequireNoSoftDelete`, a user ruling). A consumer whose base model is
  `ISoftDeletable` hard-deletes translations through a chain without that wrapper; that is the consumer's wiring.
- The open-generic scan crash — TASK-766.

## Human test plan

N/A — covered by the tests; the consumer-side proof is Symbio TASK-945.

## Implementation plan

_Drafted when picked, after the choice in criterion 1._
