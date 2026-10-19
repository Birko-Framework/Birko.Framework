---
id: TASK-679
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-19
depends-on: [TASK-168, TASK-194]
blocks: [TASK-682]
findings: [SH-M081]
pr: null
github-issue: null
jira-key: null
---

# A localized list read costs one translation query per entity — batch it

## Context

Read in source 2026-10-19 (Symbio session, for its STORY-081): `AsyncLocalizedBulkStoreWrapper.LocalizeAllAsync` loops
`entities[i] = await LocalizeAsync(entities[i], ct)`, and `LocalizeAsync` reads
`EntityTranslationFilter.ByEntityAndCulture(entity.Guid, culture)` — one query per entity. A storefront page of 48
products in English is 48 extra queries; Symbio's rules forbid exactly that shape (N+1). The sync bulk wrapper has the
same loop. Writes have the same shape (`SaveTranslationsAsync` reads then writes per field per entity), which TASK-682's
batched `SetAsync` addresses.

**Also remediates SH-M081** (handed over by [[TASK-168]], confirmed 2026-10-19): a localized order-by reads the whole matching set (`LocalizedBulkStoreWrapper.cs:86`, async twin `:84`) and then `Localize` issues one translation query per entity. Batching removes the N+1; the unbounded read itself is inherent to sorting translated text in memory, so record that limit in the spec rather than leave it implied.

## Acceptance criteria

- [ ] All four wrappers localize a list with **one** translation read: `EntityGuid IN (…)` for the current culture,
      grouped in memory; an entity without translations keeps its base values.
- [ ] The in-memory-sort path (`LocalizedOrderByHelper`) uses the same batched read.
- [ ] A test counts translation-store reads: a list of N entities in a non-default culture → 1 read (N=0 → 0 reads);
      mutation-proven by restoring the per-entity loop.
- [ ] Spec `entity-localization` and the README say list reads are batched.

## Out of scope

- Write batching — TASK-682 (`SetAsync`).
- Filters on translated fields — TASK-681.

## Human test plan

N/A — the query count is asserted by the test.

## Implementation plan

_Drafted when picked._
