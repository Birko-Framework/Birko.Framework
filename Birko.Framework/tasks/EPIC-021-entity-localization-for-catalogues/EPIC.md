---
id: EPIC-021
# status — one of: planned, in-progress, done, cancelled
status: in-progress
created: 2026-10-19
owner: ai
affects: [Birko.Data.Localization]
---

# Entity localization for real catalogues

## Area of concern

`Birko.Data.Localization` stores per-language values of an entity's fields in one translation table
(`EntityTranslationModel`: `EntityGuid`, `EntityType`, `FieldName`, `Culture`, `Value`) and swaps them in through store
wrappers driven by an ambient culture (`IEntityLocalizationContext`). Its write path is sound — a non-default-culture
update keeps the base columns (integrity tests) — and 122 tests cover it. No consumer used it until now.

Prompted 2026-10-19 by **Symbio**: its first storefront (FlowerFur) launches with an English catalogue, per-language
slugs and a Czech default (Symbio STORY-081, its TASK-770 row 20; the user chose to build it the framework way). Read
against that need in source, the project has four gaps:

1. **N+1 on list reads** — `LocalizeAllAsync` localizes one entity at a time (one translation query each).
2. **Filters on a translated field run in memory, across every scope** — `ResolveMatchingGuidsAsync` reads every
   translation of `(EntityType, FieldName, Culture)` and matches the value in memory; an entity with no translation in
   that culture never matches, even on its base value. A slug lookup per request is the worst case.
3. **No integrity key and no scoping** — no unique key on `(EntityGuid, EntityType, FieldName, Culture)`, writes are
   read-then-write per field, and the model is a plain `AbstractModel`, so a multi-tenant consumer cannot scope or purge
   translations with its tenant machinery.
4. **Only an ambient-culture surface** — code that needs a given language explicitly, all languages of one entity (an
   editor), a value lookup (slug → entity) or a uniqueness check has nothing to call.

The open spec-harvest findings on this project (TASK-168, 14 medium; TASK-194, low) are triaged first, so the work
below does not build on known defects.

## Success criteria

- A list read of N localizable entities in a non-default culture costs one translation query, not N.
- A lookup by a translated value is a database query with an index, and falls back to the base value.
- A consumer can use a tenant-scoped translation model; a duplicate `(EntityGuid, EntityType, FieldName, Culture)` row
  cannot be written.
- `IEntityTranslations` gives explicit, batched read/write/lookup/uniqueness methods, documented in the README.

## Tasks

- TASK-168, TASK-194 — triage the open findings (EPIC-014), first
- TASK-679 — batched localization of list reads
- TASK-680 — translation model generic over the consumer's type; unique key
- TASK-681 — filters on translated fields in SQL, with base-value fallback
- TASK-682 — `IEntityTranslations`: the explicit translation API
- TASK-683 — entity write and translation write share no boundary (SH-M083, spawned from TASK-168)
- TASK-684 — the translation key is too long for MySQL; a fluent mapping cannot bound the columns (spawned from TASK-680)
