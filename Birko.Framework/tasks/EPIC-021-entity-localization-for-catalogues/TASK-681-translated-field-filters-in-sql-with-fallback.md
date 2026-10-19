---
id: TASK-681
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-19
depends-on: [TASK-680]
blocks: [TASK-682]
findings: [SH-M070, SH-M082]
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

**Also remediates SH-M070 and SH-M082** (handed over by [[TASK-168]], both confirmed 2026-10-19): M070 is this task's fallback criterion, M082 its full-slice scan. M082 adds that the slice spans every tenant because `EntityTranslationModel` is not `ITenant` (TASK-680's model change is what lets a tenant wrapper scope it).

**Also SH-L087** (handed over by [[TASK-194]], confirmed 2026-10-19): the matched GUIDs become one `IN` list (`LocalizedFilterHelper.BuildGuidFilter`), which a broad match pushes past MSSQL's ~2100 parameters; nothing in the framework chunks `IN` lists. Resolving the condition as a query against the translation table is the place to replace the list with a sub-query.

**Handed over by [[TASK-680]] (user decision, 2026-10-19): the value index.** TASK-680 was to add a non-unique
`(EntityType, FieldName, Culture, Value)` index for this task's lookups, but indexing `Value` bounds the column to 255
characters on MySQL and SQL Server (rule 37) and to ~2.7 KB on PostgreSQL's btree — every translation would be capped
to serve a lookup only short values (slugs) need. Design the lookup's index here, without capping `Value` (for
example a bounded lookup column, a hash, or an index limited to the fields that are looked up).

## Acceptance criteria

- [x] Equality and `IN` (`Contains` on a list) conditions on a translated field are pushed into the translation query
      (`EntityType`, `FieldName`, `Culture`, `Value =`/`IN`), using TASK-680's value index; other predicate shapes keep
      the current in-memory path and say so in the spec.
      _Rescoped before the work, decided by the user 2026-10-19: the index is on a `ValueHash` column (SHA-256 of
      `Value`), not on `Value` — see TASK-680 for why `Value` cannot be indexed — and the lookup confirms `Value` in
      memory._
- [x] **Fallback:** an entity with no translation for that field in that culture matches on its base value (the inner
      store's own column); an entity that HAS a translation matches only on it (its base value does not also match).
      Both directions asserted.
- [x] A test proves no full scan: with translations for many entities, a lookup reads only the matching rows
      (counted); mutation-proven by restoring the in-memory predicate.
- [x] Spec and README describe the fallback rule.

## Out of scope

- The matched GUIDs as one `IN` list on the entity store (SH-L087) — [[TASK-686]] (decided by the user).
- Ordering by translated fields (`LocalizedOrderByHelper`) — unchanged.
- Scoping (tenant) — the consumer's model (TASK-680) and its decorators do it.

## Human test plan

N/A — asserted by tests.

## Implementation plan

Drafted 2026-10-19 at pick, after three decisions by the user: the fallback by **base-candidate read**, the index on
a **`ValueHash`** column, and SH-L087 (the large `IN` list) **spawned** as TASK-686.

1. **The analyzer keeps each extracted condition's original part** (`LocalizedFieldCondition.Part`, the base-column
   form) and, for `==` and `IN`, its exact values (`Values`). New extraction: `list.Contains(x.Field)` and
   `Enumerable.Contains(list, x.Field)` over strings (today refused as untranslatable).
2. **`ValueHash`** joins `IEntityTranslation`: lower-case hex SHA-256 of `Value` (`LocalizedValueHash.Of`). On
   `EntityTranslationModel` it is **computed from `Value`** (the setter is ignored), so every row carries a correct hash
   however it was written — seeded, imported, or by the upsert, which also sets it for consumer models.
   `Birko.Data.Localization.SQL` maps it and a non-unique `IX_{table}_ValueHash` on it alone (a column can be in one
   fluent index, and the key already holds the other three; the hash is selective on its own).
3. **One resolver for all four wrappers** (sync + async), replacing the four `ResolveMatchingGuids` copies. Per condition
   *i* the filter becomes `guid ∈ T_i ∨ (Part_i ∧ guid ∉ H_i)`, all ANDed with the remaining filter:
   - `T_i` — entities whose translation matches: for `==` / `IN`, a translation query on `(EntityType, FieldName,
     Culture, ValueHash IN …)` confirmed by the predicate in memory; any other shape, the field+culture slice as today;
   - `H_i` — which of the base-column candidates have a translation for that field+culture: the candidates are the
     inner store's rows matching `remaining ∧ Part_i` (a bulk inner store), and one batched translation read over them;
     a non-bulk inner store (`IStore<T>` cannot list) uses the whole slice's GUIDs instead — correct, not cheaper,
     recorded;
   - an empty `T_i` or `H_i` is reduced away, never rendered as a constant term (rule 3).
4. **Tests**: fallback both directions (`==`, `IN`, `Contains`, `!=`) on sync and async bulk and on a non-bulk inner
   store; `IN` extraction; a counted lookup over 1 000 translations reads only matching rows, red with the slice
   restored; `ValueHash` computed; SQL: the hash index exists and an equality lookup works on SQLite (live providers as
   in TASK-680).
5. **Docs**: spec (extraction matrix, the `!=` requirement inverted, fallback, cost), README, `docs/localization.md`.

## Progress log

- 2026-10-19 — picked. Decisions by the user: fallback by base-candidate read, the index on a `ValueHash` column
  (criterion 1 rescoped), SH-L087 spawned as TASK-686.
- 2026-10-19 — analyzer: every condition keeps its base-column `Part`; `==` and `IN` keep their `Values`; `IN`
  extracted from `values.Contains(x.F)`, `Enumerable.Contains(values, x.F)` and the C# 14 span overload (via
  `SpanContains`, rule 47). `ValueHash` (SHA-256 hex, `LocalizedValueHash`) on `IEntityTranslation`, computed from
  `Value` on `EntityTranslationModel`, written by the upsert for any model. `LocalizedFilterResolution` replaces the four
  `ResolveMatchingGuids` copies; a non-bulk inner store falls back through the slice (recorded). The SH-H018 crossed-pair
  tests' decoy now carries its own Slovak name: with the fallback, its untranslated base "Stolicka" became a legitimate
  match — the tests' point (writes target what reads return) is unchanged.
- 2026-10-19 — tests. Unit `LocalizationFallbackTests` 8: fallback both directions (`==`, `!=`, `Contains`,
  `StartsWith`, `IN` in three syntaxes), a remaining condition, async, a non-bulk inner store, the counted lookup
  (1 000 entities, < 10 rows), `ValueHash` computed. Mutations: fallback removed → 6 red; slice forced → only the
  counted lookup red (results stay correct, as they should); translated candidates ignored → 5 red; IN extraction
  removed → 1 red. SQL (`Birko.Data.Localization.SQL.Tests`, +2 per provider): the hash index from the catalogue, and
  the fallback + hash lookup + IN + `!=` through each provider's translator with products stored in SQL — **36/36 on
  SQLite and live PostgreSQL 16, SQL Server 2022, MySQL 8.4**. Unit 202/202, no warnings.
- 2026-10-19 — close-gate reviews (no blockers). **Correctness** (verified live on all four providers): a translation
  row with no stored `ValueHash` disappeared from `==` / `IN` (neither found by hash nor, having a translation, on its
  base) — fixed: the lookup also reads the field's null/empty-hash rows and confirms by value; tests unit + live (4
  providers), red without it. The base-column part is evaluated under the store's collation (case-insensitive on SQL
  Server / MySQL) while translations match ordinally — recorded in spec and README, not normalisable generically.
  Non-bulk path no longer does hash lookups on top of the slice it read. Time-of-check gap between the candidate read and
  the query — recorded. **Conventions**: an IN over a set with a non-ordinal comparer refused (rule 41; test red without
  the check); write paths (`Update(filter, Action)`, `Delete(filter)`, sync + async) reach the fallback match and spare a
  translated entity's base; the spec's "count is 0" scenario rewritten; a stale test remark corrected; the refusal message
  lists IN; `LocalizedFilterHelper.CombineFilters` removed as unreachable (rule 53) with its test. **Breaking**:
  `IEntityTranslation.ValueHash` is a new member; `EntityTranslationMapping.MaxTableLength` 56 → 50.
  Unit 205/205, SQL 40/40 live (SQLite, PostgreSQL 16, SQL Server 2022, MySQL 8.4), no warnings; Sandbox builds.
