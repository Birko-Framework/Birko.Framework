---
id: TASK-698
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-20
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A translation row's Guid derived from its key, so a race is retried on every provider

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). The upsert in `LocalizedTranslationRows` retries a refused create as an
update, but only a unique key refuses it, and only SQL holds one (TASK-680). Every store already refuses a second create
with the **same Guid** as `StoreUniqueViolationException` (TASK-625). If a translation row's Guid were a name-based
UUID (v5) of `(EntityGuid, EntityType, FieldName, Culture)`, every provider would refuse a racing duplicate the same way,
with no unique index: ElasticSearch, RavenDB, Cosmos DB, JSON, XML and InMemory included.

## Acceptance criteria

- [ ] Measure first: a racing create of the same key on each non-SQL provider stores two rows today (rule 54).
- [ ] The Guid is derived in one producer from the four-part key (the culture's name normalised as stored), and every
      create path uses it.
- [ ] Decide what happens to rows already stored with random Guids: read by key as today and rewritten on next save,
      left alone, or migrated. Write the decision down.
- [ ] Live or in-memory tests per provider: the race leaves one row, holding the last writer's value.
- [ ] The consumer-supplied translation models (TASK-680) keep working; a model whose Guid the caller sets is refused or
      honoured, and the choice is decided and written down.

## Out of scope

- Indexes (TASK-699), transactions (TASK-700).

## Human test plan

N/A — asserted by the suites.

## Implementation plan

_Populated by `/tasks plan TASK-698` — leave empty until then._
