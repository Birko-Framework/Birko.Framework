---
id: TASK-503
parent: EPIC-014
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
related: [TASK-498]
findings: [FIELD-016]
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.MongoDB]
---

# MongoDB's native `PropertyUpdate` writes under the C# name, ignoring `[BsonElement]` / `_id` mapping

Found at TASK-498's close gate (correctness review); pre-existing, now reached by `$inc` as well as `$set`.

## Context

`Birko.Data.MongoDB/Stores/MongoPropertyUpdateTranslator.cs` (before TASK-498, the same code inline in both stores)
builds `Builders<T>.Update.Set(name, …)` / `.Inc(name, …)` from the **string** `PropertyInfo.Name`. A property renamed
with `[BsonElement("x")]`, a class map `MapMember(...).SetElementName(...)`, or `Id` mapped to `_id` is therefore
written under the C# name: the update creates a new field and leaves the mapped one untouched, with no error — a
write that reports success while persisting nothing the reader sees (rule 22).

The fix is the expression overloads (`Builders<T>.Update.Set(Expression<Func<T, TField>>, …)` / `.Inc(...)`), which
render through the serializer and honour the mapping, or resolving the element name from the class map.

## Acceptance criteria

- [ ] A Set and an Increment on a `[BsonElement]`-renamed property update the mapped element (sync and async).
- [ ] The string-representation refusal (TASK-498) keeps working on the mapped member.
- [ ] Render test in `MongoPropertyUpdateTranslatorTests`; a live twin under the family's `BIRKO_MONGO_*` gate.

## Out of scope

- Elasticsearch's camelCase field naming, a separate mechanism.

## Implementation plan

_Populated by `/tasks plan TASK-503` — leave empty until then._
