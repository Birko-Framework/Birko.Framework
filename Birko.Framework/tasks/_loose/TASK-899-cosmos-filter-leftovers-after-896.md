---
id: TASK-899
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-896]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB filters: leftovers after TASK-896

## Context

Found by TASK-896 (2026-11-10):
- **One translator for store and view:** `CosmosViewStore` still renders its own SQL. Moving it onto the LINQ provider
  needs a `TView` → source lambda rewrite (renamed and nested paths, TASK-843), the `root` alias, and replacing
  TASK-839's single-parameter `ARRAY_CONTAINS`. A refactor; null handling is already shared.
- **A non-nullable member missing from a document:** C# reads the type's default, Cosmos treats it as undefined. Not
  covered.
- **A `Contains` list holding null** is now `null OR IN(..)`, which cannot be chunked, so past ~13k values it is refused
  rather than split.
- **Nested lambdas** (`Any` / `All` predicates) with nullable members were not measured.
- **View-store aggregates** (TASK-897, measured): decimal SUM/AVG are read through the serializer's double (0.1 + 0.2 =
  0.30000000000000004; 1/3 with 16 digits), and a DateTime GROUP BY groups the stored text, so a local and an
  unspecified time with equal ticks are two groups (C#: one). The store fixed both; the view needs a post-processing
  pass or a refusal, and the grouping change touches `ResolveOrderKey`.
- Query cost / index use of `IS_DEFINED` / `IS_NULL` was not measured on a real account.

## Acceptance criteria

- [x] Measure each (rule 54).
      → Measured 2026-11-10 live on the emulator against C# / LINQ:
        - Missing non-nullable member: 34 of 44 store shapes wrong on all 7 paths (`A == 0`, `A != 5`, `!(A > 3)`, `!Del`,
          `E == New`, `A + 1 == 1`…); views 13 of 19.
        - A `Contains` list holding null past ~13k: refused on every path.
        - Nested lambdas: 23 of 30 wrong (10 `All` threw; `Any`/`Count`/`Where` skipped null or missing members).
        - View aggregates: decimal SUM 0.30000000000000004, decimal AVG 16 digits, DateTime GROUP BY split equal ticks
          into 3 groups, sort by a DateTime group key sorted stored text.
        - View translator vs store: 165 shapes the store answers as C#; the view refuses 54 by name and answers none
          wrongly. `IS_DEFINED` / `IS_NULL` cost: the emulator charges 1 RU per page and gives no index metrics.
- [x] Fix or record each; decide the single-translator refactor on its cost.
      → Missing member: `CosmosFilterRewrite.IsDefaultable` — where the default passes the test, `NOT IS_DEFINED(m) OR
        test`; where it fails, the plain test (keeping its index), guarded with `IS_DEFINED` only under `!`; computed
        operands read `IS_DEFINED(m) ? m : default`. Same for `Contains` and bare bools under `!`.
      → Long lists holding null: `TryAbsentOrIn` + `CosmosInListChunking` split the IN and put the absent leg in exactly
        one chunk (disjoint); a negated such list past the limit stays refused.
      → Nested lambdas: predicates take the same rewrite; `All(p)` is `!Any(!p)`. An aggregate whose filter holds one
        stays refused (TASK-876).
      → View aggregates: decimal SUM rounded as the store does; decimal AVG refused by name (as TASK-892's SQL server
        views); DateTime GROUP BY on the wall clock through one `CosmosAggregationHelper.DateTimeGroupKey`; sort by a
        DateTime group key refused by name (Cosmos rejects both forms, 2206 / SC2103).
      → Single translator: **not done, recorded** — the gap is capability (54 refusals), not correctness; the move costs
        the `TView` → source rewrite, the `root` alias, inline literals instead of bound parameters, TASK-839's
        single-parameter `ARRAY_CONTAINS` and 8 test files. Filed with the other capability items as TASK-903.
        Query cost recorded as unmeasured.
- [x] Tests, red before each fix.
      → `Task899FilterLeftoversLiveTests` (+ `Task899AbsentLegChunkingLiveTests`, offline `Task899FilterRenderingTests`),
        `Task899CosmosViewAggregateTests`, `Task899CosmosViewFilterTests`: red 9 of 11 and 7 of 8 with the production files
        reversed, green 11/11 and 8/8. Live: CosmosDB 424, Views 161, ViewModel 5, BackgroundJobs 28, Migrations 44,
        Sync 4, Workflow 8. No consumer defines a view; Symbio's Cosmos filters return the same rows on Birko-written
        documents.
      → **Found, filed:** TASK-902 (P1: Cosmos DateTime filters compare stored text — `x.T > 10:00:00Z` misses the
        10:00:00.5 document), TASK-903 (capability: nested-predicate aggregates, exact view decimal AVG, single
        translator, `IS_DEFINED` cost on a real account).

## Out of scope

- Null semantics in flat filters (TASK-896, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
