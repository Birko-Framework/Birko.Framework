---
id: TASK-680
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-19
depends-on: [TASK-168, TASK-194]
blocks: [TASK-681, TASK-682]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The translation model is fixed and unkeyed — make it the consumer's type, with a unique key

## Context

Read in source 2026-10-19 (Symbio session, for its STORY-081):

- The wrappers are typed on `EntityTranslationModel : AbstractModel`. A multi-tenant consumer cannot scope or purge
  translations with its tenant machinery (Symbio's tenant filter, soft delete and tenant purge act on `TenantEntity`
  rows); the rows are global. Symbio needs its translation rows to be its own tenant entity.
- Nothing prevents two rows for one `(EntityGuid, EntityType, FieldName, Culture)`: writes read, then create or update,
  per field, with no key — two concurrent writers can both create. Reads then pick one arbitrarily.

## Acceptance criteria

- [ ] The four wrappers (and TASK-682's API) are generic over `TTranslation : EntityTranslationModel, new()`, so a
      consumer supplies its own model (e.g. one deriving from its tenant base); `EntityTranslationModel` stays the
      default and existing call sites compile unchanged (a non-generic alias or default overload).
- [ ] The model declares a **unique** index on `(EntityGuid, EntityType, FieldName, Culture)` and a non-unique index on
      `(EntityType, FieldName, Culture, Value)` for value lookups (TASK-681); both are created by schema-ensure on the
      SQL providers, measured on SQLite and PostgreSQL.
- [ ] A concurrent double-write of one key yields one row (the second updates or fails loudly — the choice is written
      down), asserted on a real database; mutation-proven by removing the unique index.
- [ ] README: how a tenant-aware consumer plugs in its own model.

## Out of scope

- Value-length limits for `Value` on indexed providers — note the index's provider limits; decide if a test shows a problem.

## Human test plan

N/A — asserted by tests.

## Implementation plan

_Drafted when picked._
