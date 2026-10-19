---
id: TASK-682
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-19
depends-on: [TASK-679, TASK-680, TASK-681]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `IEntityTranslations` — an explicit, batched API for working with translations

## Context

The project offers one surface: store wrappers that translate according to an **ambient** culture. Symbio's storefront
work (its STORY-081, 2026-10-19) needs operations that surface cannot express — a given language per request without
swapping a context, every language of one entity for an admin editor, slug → entity, "is this slug taken in English?",
and batched writes. The user asked for these methods to live in the framework.

Proposed surface (names may be refined at plan time; each method batched, generic over the TASK-680 model):

| Method | For |
|---|---|
| `GetAsync(entityType, ids, culture)` → `id → field → value` | one language for a list, one query |
| `GetAllCulturesAsync(entityType, id)` → `culture → field → value` | an editor showing every language |
| `SetAsync(entityType, id, culture, fields)` — upsert; a `null` value removes that field | saving one language of an entity in one batched write |
| `RemoveCultureAsync(entityType, id, culture)` / `RemoveAllAsync(entityType, ids)` | dropping a language; delete and purge |
| `FindEntityAsync(entityType, field, culture, value)` → `Guid?` (with TASK-681's base-value fallback through the entity store) | slug → entity |
| `ExistsAsync(entityType, field, culture, value, exceptEntityGuid)` | uniqueness checks (a slug free in that language) |
| `LocalizeAsync<T>(entities, culture)` where `T : ILocalizable` | applying a given language to loaded entities, batched, with fallback |

**From [[TASK-680]]:** the translation model is any `TTranslation : AbstractModel, IEntityTranslation` (members as public properties, `LocalizedTranslationRows.RequireQueryableModel`; rows made with the store's `CreateInstance()`); build this API on that constraint and on `LocalizedTranslationRows` (generic, concrete-type filters, a create refused by the unique key retried as an update).

## Acceptance criteria

- [ ] `IEntityTranslations` (+ implementation over any Birko async store of the translation model) with the methods
      above, a DI registration extension, and a README section with an example per method.
- [ ] Every read method costs one query regardless of how many ids/fields (counted in tests); `SetAsync` writes one
      entity-language in one batched call and respects TASK-680's unique key.
- [ ] The wrappers reuse the same internals (no second copy of the lookup/localize code — cf. TASK-452's three copies of
      the detach helper).
- [ ] Tests on a real SQL store for each method, incl. fallback in `FindEntityAsync`/`LocalizeAsync` and `exceptEntityGuid`
      in `ExistsAsync`; mutation-proven where a method guards something (uniqueness, fallback).

## Out of scope

- Any UI.
- Machine translation.

## Human test plan

N/A — asserted by tests.

## Implementation plan

_Drafted when picked._
