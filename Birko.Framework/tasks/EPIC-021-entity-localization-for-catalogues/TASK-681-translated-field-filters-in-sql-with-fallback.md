---
id: TASK-681
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-19
depends-on: [TASK-680]
blocks: [TASK-682]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A filter on a translated field reads every translation into memory, and never matches an untranslated entity

## Context

Read in source 2026-10-19 (Symbio session, for its STORY-081): `ResolveMatchingGuidsAsync` (all wrappers) reads **every**
translation of `(EntityType, FieldName, Culture)` — no value predicate in the query, no scope — and applies the
condition in memory (`condition.ValuePredicate(t.Value)`), then intersects Guids. Two consequences:

- **Cost:** each call scans the whole translated field across every tenant. Symbio's slug lookup (`Slug == x` in
  English, on every public product request) is the worst case.
- **No fallback:** an entity with no translation in the current culture never matches — even on its base value — so an
  untranslated product is unreachable by slug under English, while a read of it would show its base text.

## Acceptance criteria

- [ ] Equality and `IN` (`Contains` on a list) conditions on a translated field are pushed into the translation query
      (`EntityType`, `FieldName`, `Culture`, `Value =`/`IN`), using TASK-680's value index; other predicate shapes keep
      the current in-memory path and say so in the spec.
- [ ] **Fallback:** an entity with no translation for that field in that culture matches on its base value (the inner
      store's own column); an entity that HAS a translation matches only on it (its base value does not also match).
      Both directions asserted.
- [ ] A test proves no full scan: with translations for many entities, a lookup reads only the matching rows
      (counted); mutation-proven by restoring the in-memory predicate.
- [ ] Spec and README describe the fallback rule.

## Out of scope

- Ordering by translated fields (`LocalizedOrderByHelper`) — unchanged.
- Scoping (tenant) — the consumer's model (TASK-680) and its decorators do it.

## Human test plan

N/A — asserted by tests.

## Implementation plan

_Drafted when picked._
