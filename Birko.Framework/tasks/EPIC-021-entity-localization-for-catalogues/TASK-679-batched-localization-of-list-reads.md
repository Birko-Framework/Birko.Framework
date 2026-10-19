---
id: TASK-679
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] All four wrappers localize a list with **one** translation read: `EntityGuid IN (…)` for the current culture,
      grouped in memory; an entity without translations keeps its base values. — Met by the two bulk wrappers; the two
      single-entity wrappers have no list read (each call returns one entity, at one translation read).
- [x] The in-memory-sort path (`LocalizedOrderByHelper`) uses the same batched read.
- [x] A test counts translation-store reads: a list of N entities in a non-default culture → 1 read (N=0 → 0 reads);
      mutation-proven by restoring the per-entity loop.
- [x] Spec `entity-localization` and the README say list reads are batched.

## Out of scope

- Write batching — TASK-682 (`SetAsync`).
- Filters on translated fields — TASK-681.

## Human test plan

N/A — the query count is asserted by the test.

## Implementation plan

Drafted 2026-10-19 at pick, against `451c6288` (TASK-168 and TASK-194 already moved every row read into
`LocalizedTranslationRows`).

1. `LocalizedTranslationRows.ReadMany` / `ReadManyAsync(store, guids, entityType, culture, fields)` →
   `Guid → (field → value)`: one read per chunk of GUIDs, filter `guids.Contains(t.EntityGuid) && t.EntityType == type
   && t.Culture == culture` over a `List<Guid>` (the shape providers turn into `IN`, as `BuildGuidFilter` does).
   **Chunked at 500 GUIDs**: the in-memory-sort path localizes the whole matching set, and one `IN` list for all of it
   would hit the provider's parameter limit (MSSQL ~2100, the SH-L087 problem) — so "one read" holds for a list of up
   to 500 entities, and N entities cost ⌈N/500⌉. Duplicate GUIDs read once; null GUIDs are skipped.
2. `LocalizeAll` / `LocalizeAllAsync` (both bulk wrappers): non-default culture → one `ReadMany` for the list, then
   `Detach` + `Restore` per entity from the grouped rows; an entity with no rows keeps its base values (still a
   detached copy, the SH-H016 rule). This covers every list read, the in-memory-sort path included (it calls the same
   method). The two single-entity wrappers return one entity per read and already cost one query: unchanged, recorded.
3. Tests: count translation-store reads (a counter on the test doubles): N = 3 → 1, N = 0 → 0, default culture → 0,
   the sort path → 1, N = 1 001 → 3; values and fallback asserted alongside. Mutation: restore the per-entity loop.
4. Spec + README: list reads are batched (chunk size stated); the in-memory sort still loads the whole matching set
   (SH-M081's remaining half, inherent — recorded as a limit, not hidden).

Not here: the per-row translation read the filter-based `Update(filter, Action)` does (TASK-168) is a write path —
batching writes and their reads is TASK-682.

## Progress log

- 2026-10-19 — picked and planned. `LocalizedTranslationRows.ReadMany` / `ReadManyAsync` + `Apply`; `LocalizeAll` /
  `LocalizeAllAsync` in both bulk wrappers read once per 500 entities. The two single-entity wrappers return one
  entity per read, so "a list" applies to the bulk wrappers; they keep one read per entity read (criterion 1 read that way).
- 2026-10-19 — `LocalizationBatchedReadTests` 8: **5 red with the per-entity loop restored, 3 pins** (empty list,
  default culture, detached copy — true either way). 186/186 green, no warnings. Spec and README updated; the
  in-memory sort's whole-set load stated as a limit (SH-M081's remaining half).
- 2026-10-19 — conventions review (no blockers). Fixed: rule 16 — the single-entity `Localize` (all four wrappers) now
  goes through the same `ReadMany` + `Apply`, so one producer turns rows into a translated entity; `Apply` constrained to
  `AbstractModel`; field selection in one `OnlyFields`; stale `LocalizeAll` summary; async test for more than one batch
  and for an empty list. Recorded, not measured: the 500-GUID batch on a SQL provider — `EntityTranslationModel` has no
  SQL mapping yet, so no SQL store can hold it; the measurement is handed to TASK-680 (rule 54). 187/187 green.
- 2026-10-19 — correctness review (no blockers, no should-fix). Fixed: `Apply` validates the entity's declaration on
  every read again, not only when it has rows; `Many`'s comment no longer claims `BuildGuidFilter`'s shape. New test
  pins a mixed list (null GUID kept as the same instance, an undeclared field, another type and another culture
  ignored, a duplicate entity read once and copied twice). Noted: a single `Read(guid)` now asks the provider for a
  one-element `IN` instead of an equality; every translator handles `List.Contains` (read, not run live — with the
  SQL measurement, TASK-680). 188/188 green, no warnings.
