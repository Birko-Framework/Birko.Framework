---
id: TASK-896
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-885]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB store reads give wrong rows for `HasValue`, negated orderings over null and `string.Compare`

## Context

Found by TASK-885 (2026-11-10), measured on the emulator against C#, 34 filter shapes through the store's LINQ provider
(reads, and so also the guarded writes and the aggregate filter since TASK-876):
- `x.Score.HasValue` matches every row (6 of 6 where C# matches 4). Silently wrong.
- `!(x.Score > 20)` misses the null rows (C#: `!(null > 20)` is true). The view translator (`CosmosFilterTranslator`) has
  the same defect. TASK-867's fold covers orderings against a null *constant*, not a null column under NOT.
- `string.Compare(x.Code, "c2") > 0` misses a row.
- `string.IsNullOrEmpty` is refused by the store (the view translator answers it).
This also blocks giving `CosmosViewStore` the store's translator (one producer), which TASK-885 kept apart on purpose.

## Acceptance criteria

- [x] Reproduce (rule 54) on reads, counts, the writes the guard allows and aggregates, store and view.
      → Measured 2026-11-10 live, 10 documents (two omit the nullable members, so "missing" is apart from "stored null"),
        93 shapes × 7 paths (async/sync read and count, aggregate WHERE, the read behind `Update(filter, Action)`,
        Delete): 53 silently wrong, 14 threw `DocumentQueryException`, the same on every path.
        - `HasValue` rendered `IS_DEFINED`, true for a stored null (8 rows vs C# 5).
        - Every negated ordering (also inside groups; enum, `DateTime?`, `decimal?`) dropped null and missing rows.
        - Every operator is undefined on a missing property, so `== null`, `!=`, `IN (.., null)`, `NOT IN` missed them.
        - Cosmos `??` replaces only undefined, never null.
        - `string.Compare` / `CompareTo` rendered Cosmos's code-point order (`"C7"` vs `"c2"` reversed vs culture order).
        - `IsNullOrEmpty`, `IsNullOrWhiteSpace`, `GetValueOrDefault`, `CompareOrdinal` threw.
- [x] Each wrong shape is translated exactly (a pre-pass rewrite, as `NullOrderingFold` does) or refused by name.
      → One pre-pass `CosmosFilterRewrite.Prepare` (`SpanContains`, `DateTruncation`, `NullOrderingFold`, new
        `NullSemantics`) at all 10 store call sites and in `CosmosFilterTranslator.Translate`: store and view share one
        producer for null handling. "Has a value" = `IS_DEFINED(x) AND NOT IS_NULL(x)`, "null" = its complement;
        `HasValue`, `== / != null`, guarded `!=`, null-equals-null between members, lists holding null, `??` /
        `GetValueOrDefault`, `IsNullOrEmpty` rewritten; a comparison under an odd number of `!` is guarded, a positive one
        renders as before. `CompareOrdinal` / `Compare(.., Ordinal)` against 0 translate (null first).
      → Refused by name before any request: `IsNullOrWhiteSpace` (Cosmos `TRIM` keeps U+00A0, U+0085), culture and
        ignore-case comparisons, an ordinal comparison with a value at or above U+D800 (code-point vs UTF-16 order), two
        members or a computed member compared, a comparison result against anything but 0. **Breaking:** these threw an
        SDK exception or answered wrongly before.
- [x] Decide on measurement whether the view store can then use the store's translator; do it or record why not.
      → Not in this task. The blocker TASK-885 recorded is gone and null handling now has one producer; what remains is
        rendering. Moving views onto the LINQ provider means rewriting the `TView` lambda into the source type (renamed
        and nested paths, TASK-843), LINQ's `root` alias, and giving up TASK-839's single-parameter `ARRAY_CONTAINS`: a
        refactor, recorded in `Birko.Data.CosmosDB.Views/CLAUDE.md` and filed as TASK-899.
- [x] Tests, red before the fix.
      → `Task896NullSemanticsLiveTests` (91 translated + 12 refused shapes on all 7 paths vs C#; a refused write leaves
        rows unchanged; rendering, idempotence, refusals) and `Task896CosmosViewNullSemanticsTests` (28 live shapes through
        a renamed view field, read and count, a grouped WHERE). Red with HEAD's stores and view store: 2 of 18 (both live
        matrices) and 31 of 56. Green 18/18 and 151/151. Two existing view tests updated on purpose.
      → Live: CosmosDB 398, Views 151, BackgroundJobs 28, ViewModel 5, Migrations 44, Sync 4, Workflow 8.
      → Consumers: Cosmos is selectable in Symbio but not configured anywhere; no Symbio filter uses a refused shape;
        ~52 filters render differently and return the same rows on Birko-written documents (nulls stored explicitly).

## Out of scope

- Aggregates themselves (TASK-885, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
