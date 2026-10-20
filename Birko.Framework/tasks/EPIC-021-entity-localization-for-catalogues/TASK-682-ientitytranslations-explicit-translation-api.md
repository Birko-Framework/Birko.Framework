---
id: TASK-682
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-19
depends-on: [TASK-679, TASK-680, TASK-681, TASK-690]
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
- [ ] Every method costs a fixed number of queries, independent of the number of fields, counted in tests: one
      translation read per 500 ids for `GetAsync`/`LocalizeAsync`, 1 for `GetAllCulturesAsync`, a fixed 4 for
      `FindEntityAsync`/`ExistsAsync` with fallback. `SetAsync` writes one entity-language as 1 read + at most 3 bulk
      calls (TASK-690) and respects TASK-680's unique key. *(Reworded 2026-10-20, decided by the user: the original
      "one query regardless of ids" cannot hold under the 500-id chunking or across two stores.)*
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

_Drafted 2026-10-20 (Plan agent, against the code at `c7ac97fc`). Decided by the user 2026-10-20: the split (steps 1–3
→ TASK-690), the AC 2 reading (fixed counts, see the criterion), D1 (generic per entity) and D8 (DI in this project).
D2–D7 were taken as recommended; nobody objected._

### Decisions (recommendation first)

- **D1 Shape.** Use `IEntityTranslations<T>` (`T : AbstractModel, ILocalizable`), implemented by
  `EntityTranslations<T, TTranslation>` over `IAsyncBulkStore<TTranslation>`, the entity store `IAsyncBulkStore<T>` and
  `IEntityLocalizationContext` (`DefaultCulture` only).
  - `entityType` comes from `LocalizedTranslationRows.EntityTypeOf<T>()`, never a caller's string (rule 16).
  - Fields are validated once in the constructor.
  - Alternative: a non-generic interface with a string `entityType`, where a typo returns empty results.
- **D2 Culture.** Take a `CultureInfo` and store its `.Name`, as the wrappers do.
- **D3 Default culture.** `SetAsync` refuses it: the wrappers never read such a row (rule 22). `Get*` return rows only.
  `LocalizeAsync` is a no-op. `Find`/`Exists` query the base column.
- **D4 Naming.** `RemoveAllAsync(ids)` → `RemoveEntitiesAsync(ids)`: rule 1 reserves `*All` for writes to every row.
- **D5 Ambiguous `FindEntityAsync`.** Throw `InvalidOperationException` naming both GUIDs. The final read uses
  `limit: 2` (rule 26).
- **D6 `ExistsAsync` honours fallback.** It answers whether `FindEntityAsync` would resolve the value to an entity other
  than `exceptEntityGuid`, through one shared producer. Otherwise an untranslated product's base slug reads as free.
- **D7 Entity store.** A localized wrapper anywhere in its `IStoreWrapper` chain is refused at construction, because it
  would re-apply the ambient culture. Tenant and soft-delete wrappers stay.
- **D8 DI.** `AddEntityTranslations<T, TTranslation>()` lives in this project, which then needs
  `Microsoft.Extensions.DependencyInjection.Abstractions`. That package is already pinned and matches Data.Repositories
  and Data.Tenant. Alternative: a separate `.DependencyInjection` project.

### Steps

1–3. → deferred to **TASK-690**: batched writes, `DeleteMany` / `DeleteCulture`, the shared `LocalizeAll` / `ReadAllCultures` / `OnlyFields`, and the wrappers switched over. This task builds on them.
4. **`Translations/IEntityTranslations.cs` + `EntityTranslations.cs`** (registered in `.projitems`). Each method is a
   thin call:
   - `GetAsync` → `ReadManyAsync`, declared fields only.
   - `GetAllCulturesAsync` → `ReadAllCulturesAsync`.
   - `SetAsync` → the batched save. It refuses undeclared fields (rule 41), the default culture and `Guid.Empty`. Empty
     fields make no call.
   - `RemoveCultureAsync` / `RemoveEntitiesAsync` → TASK-690's `DeleteCulture` / `DeleteMany`.
   - `FindEntityAsync` and `ExistsAsync` share one producer. It builds `x.<field> == value` (`&& x.Guid != except`) and
     runs `LocalizedFilterResolution.RewriteAsync`, the TASK-681 hash lookup and fallback, reused. It then reads the
     entity store with `limit: 2`, or `limit: 1` for `ExistsAsync`. Check that the analyzer accepts a
     `ConstantExpression`; otherwise close over a local.
   - `LocalizeAsync` → `LocalizeAllAsync`, which returns detached copies (rule 27).
5. **`EntityTranslationsServiceCollectionExtensions`.**
   - `AddEntityTranslations<T, TTranslation>(lifetime = Scoped)` resolves both stores and the context.
   - A factory overload takes explicit stores.
   - `AddEntityTranslations<T>()` uses `EntityTranslationModel`.
   - Null arguments throw `ArgumentNullException`.
6. **Tests.**
   - Unit (`EntityTranslationsTests`): add create/update/delete counters to the InMemory test stores.
     - Pin the counts: Get with 3 ids and 2 fields = 1, 501 ids = 2; GetAllCultures = 1; Set (new + changed + null) =
       1 + 1 + 1 + 1; Localize = 1; Find = 4.
     - Refusals: undeclared field, default culture, ambiguous match, localized entity store.
     - `RemoveCulture` leaves other cultures and types alone, and `RemoveEntities([])` deletes nothing.
     - A racing writer leaves one row, and DI resolves the service.
   - SQL live: extend `TranslationTableTests` with a counting delegating store.
     - Cover every method: a translated hit; a base-fallback hit; an entity translated to another value is *not* found
       by its base value; `exceptEntityGuid`.
     - Racing `SetAsync` is skipped where `HoldsTheKey` is false (MySQL until TASK-684).
   - Mutations, each red: drop the `guid ∉ H` term; drop the `except` clause; Exists ignoring fallback; no unique-key
     retry; per-field save restored; empty-ids guard removed; per-entity localize loop restored.
7. **Docs.**
   - Project README: an "Explicit API" section, with an example per method, the counts, the default-culture rules and DI.
   - Project and test-project CLAUDE/README files.
   - `docs/localization.md`.
   - `/specs regen` of entity-localization.
   - Root CLAUDE.md Recent Updates (+ CHANGELOG roll).

### Risks

- A refused bulk create inside a caller's PostgreSQL transaction aborts it, as the single create does today. Reported,
  not fixed.
- TASK-681's limits are inherited: the base column follows the collation (case-insensitive on MSSQL/MySQL), and the
  candidate-read race remains.
- API writes share no boundary with entity writes (TASK-683).

### Split

Split 2026-10-20 (decided by the user): steps 1–3 are TASK-690, which goes first.
