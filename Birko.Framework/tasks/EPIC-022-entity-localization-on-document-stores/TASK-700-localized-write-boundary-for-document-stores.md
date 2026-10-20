---
id: TASK-700
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

# A localized write boundary for MongoDB, RavenDB and Cosmos DB

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). `ILocalizedWriteBoundary` has one atomic implementation,
`SqlLocalizedWriteBoundary` (TASK-683). Any other pairing is best effort, so a failed translation write leaves the
entity written. MongoDB (`MongoDbUnitOfWork`), RavenDB (`RavenDbUnitOfWork`, cluster-wide session) and Cosmos DB
(`CosmosDbUnitOfWork`, a single-partition transactional batch) each have a unit of work.

## Acceptance criteria

- [ ] Measure first what each unit of work can hold: both collections or containers in one transaction? Cosmos's batch
      is limited to one partition key, which may rule it out for an entity and its translations.
- [ ] A boundary per provider that can hold both, joining a caller's unit of work and refusing or degrading where it
      cannot. Each decision is written down.
- [ ] Live tests per provider: a failed translation write rolls the entity write back.

## Out of scope

- ElasticSearch, JSON, XML, InMemory: no transactions.

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Populated by `/tasks plan TASK-700` — leave empty until then._
