---
id: TASK-699
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

# MongoDB: declare the translation key and ValueHash indexes

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). On SQL, `EntityTranslationMapping` declares the unique key
`(EntityGuid, EntityType, FieldName, Culture)` and the `ValueHash` index (TASK-680/681). On MongoDB nothing declares
them, so lookups by value scan the collection. MongoDB can hold a unique index (`MongoDBIndexManager`), and its stores
already translate a duplicate key (E11000) into `StoreUniqueViolationException`, so the upsert's retry would work there
once the index exists.

## Acceptance criteria

- [ ] One call creates both indexes on a MongoDB translation collection, idempotently, as `GrantSqlIndexes` does for
      SQL; whether this lives in a new project (`Birko.Data.Localization.MongoDB`) is decided first.
- [ ] Live MongoDB: the indexes exist (read back from the server), a duplicate key is refused typed, a racing upsert
      leaves one row, and a lookup by value uses the index (explain plan).
- [ ] Interplay with TASK-698 recorded: with deterministic Guids the unique index is a second line of defence.

## Out of scope

- Other document stores: they cannot hold a unique index (their managers refuse it).

## Human test plan

N/A — asserted by the live suite.

## Implementation plan

_Populated by `/tasks plan TASK-699` — leave empty until then._
