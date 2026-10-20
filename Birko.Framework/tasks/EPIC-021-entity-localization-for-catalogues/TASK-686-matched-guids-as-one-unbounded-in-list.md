---
id: TASK-686
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-19
depends-on: [TASK-681]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-L087]
pr: null
github-issue: null
jira-key: null
---

# A filter on a translated field reaches the entity store as one unbounded `IN` list

## Context

Spawned from [[TASK-681]] (decided by the user, 2026-10-19), which took SH-L087 over from [[TASK-194]]. A filter on a
translated field is resolved against the translation store first; the matching entity GUIDs then reach the entity
store as one `IN` list (`LocalizedFilterHelper.BuildGuidFilter`, and since TASK-681 also a `NOT IN` list of base-value
candidates that have a translation). A broad match — a `Contains` over a large catalogue — pushes the list past MSSQL's
~2 100 parameters (and MySQL's packet size), so the read fails at the driver. Nothing in the framework chunks `IN` lists,
and the wrappers cannot join across two stores (the translations may live in another one).

## Acceptance criteria

- [x] Above a set size (the translation reads already batch at `LocalizedTranslationRows.ReadBatchSize`), the entity
      query runs in chunks and the results are merged: reads concatenate, `Count` sums, and ordering / paging that
      cannot be pushed into each chunk is applied in memory, as the localized-sort path already does
- [x] Filter-based `Update` and `Delete` apply chunk by chunk with the same coverage
- [x] A live MSSQL test with a match over 2 100 entities passes, red with the chunking removed

## Out of scope

- The translation-side reads — batched by [[TASK-679]].

## Human test plan

N/A — asserted by the live suite.

## Outcome (2026-10-20)

All criteria met, measured.

- **Design.** Above `ReadBatchSize` (500) GUIDs across every T and H, `LocalizedFilterResolution` rewrites the filter
  to `remaining ∧ guid ∈ S`, with S = ∩ (T ∪ (C \ H)), and returns disjoint chunked queries (`LocalizedQueries<T>`).
  `LocalizedChunkedReads` is the one runner.
  - Reads concatenate, counts sum, and a single read takes the first chunk with a match.
  - Find/Exists count their limit across chunks.
  - An empty S runs no query.
  - Ordered and paged reads load the whole match, then sort and page in memory (D4).
  - `Update(Action)` and `Delete(filter)` read every chunk. A native update resolves and runs inside the boundary.
  - Collection-update base values are read per 500 (D3).
- **Plain stores (D2, refined at the close review).** A store that cannot list keeps its one query up to
  `NonListingQueryLimit` = 2 000 GUIDs and is refused beyond.
  - The first version refused it at 500. Its H is the field's whole translation slice, so that refused whole catalogues
    whose filters had worked before.
- **Tests:**
  - Unit 295/295: LocalizationChunkedFilterTests 18, with every inner filter's GUID list checked to stay ≤ 500 through
    `SeenFilters`.
  - SQL 120/120 live: a 2 200-entity match counted, paged and deleted on all four providers, then cleaned up.
  - The solution builds with no new warnings; Sandbox and Symbio.Birko build.
- **Mutations** (13, each red):
  - No chunking: unit 10 + live 1, which is SQL Server, past its parameter limit.
  - Always chunk (2); the empty set falling back to `remaining`; the fallback set ignored (7); conditions unioned (1).
  - Count of the first chunk; read-all of the first chunk (5); the limit per chunk; the page per chunk (2).
  - The native update outside the boundary; base values in one list.
  - The non-listing limit at 500 (2); the refusal removed.
- **Close review** (8 findings).
  - Fixed:
    - the plain-store regression;
    - the native update resolving outside the boundary;
    - the live test's leftover rows;
    - the `RewriteFilter` visibility change, now marked **Breaking** (no consumer subclasses a wrapper).
  - Kept, as decided: in-memory ordering once chunked (D4; the README states the .NET comparison).
  - Deferred, performance only, recorded here: the candidate entities are read once more chunk by chunk, and orphan
    translation rows add chunk round trips.
- **Spec harvest:** entity-localization regenerated, and corrected for the two review fixes.

## Implementation plan

_Drafted 2026-10-20 (Plan agent). Decided by the user 2026-10-20:_
- _D1: rewrite only above the budget._
- _D2: a plain `IStore` inner store over the budget is refused._
- _D3: the collection update's base-value read is chunked too._
- _D4: chunked ordered/paged reads load every chunk, then sort and page in memory._

**AC 1 addendum (D2):** a non-listing inner store cannot be chunked, because its exclusion set has no candidates to
invert. Past the budget it is refused with `NotSupportedException`.

### Design

- `Rewrite*` builds `remaining ∧ ∧ᵢ (guid ∈ Tᵢ ∨ (Partᵢ ∧ guid ∉ Hᵢ))`. Hᵢ is a subset of the candidates Cᵢ (rows
  matching `remaining ∧ Partᵢ`, already loaded). So, under `remaining`, `Partᵢ ∧ guid ∉ Hᵢ` is `guid ∈ Fᵢ` with
  Fᵢ = Cᵢ \ Hᵢ.
  - The filter therefore becomes `remaining ∧ guid ∈ S`, with S = ∩ᵢ (Tᵢ ∪ Fᵢ), at no extra read.
- When Σ|Tᵢ| + Σ|Hᵢ| exceeds `ReadBatchSize`, S is split into disjoint chunks of `ReadBatchSize`, one query per chunk.
  If S is empty there are zero queries, never `remaining` alone (rules 2 and 3). Below the budget, today's single
  query is unchanged.
- New producer `LocalizedQueries<T>` (one expression, N disjoint ones, or none). `LocalizedChunkedReads` (sync and
  async) is the only code that runs them: read all, read first, count (sum), read up to a limit, and a paged/ordered
  read (D4: all chunks, then sort and page in memory).
- Writes:
  - `Update(Action)` and `Delete(filter)` read through `ReadAll` inside the boundary.
  - Native `Update(PropertyUpdate)` loops the chunks, inside the boundary when there is more than one.
- `Find` / `Exists`: a running limit across chunks.
- D3: `ReadBaseValues(items)` chunked by `ReadBatchSize`.

### Tests

- **Unit, sync and async:** every inner `List<Guid>` holds ≤ 500 GUIDs.
  - Exact results with no duplicates for a large T, a large H, two conditions, and an empty S (zero calls).
  - `Count`; paging with a non-localized order, a localized order and no order.
  - `Find` across chunks; `Update`, native `Update` and `Delete` covering every chunk.
  - Below-budget pin; the plain-store refusal.
- **Live, all four providers:** 1 100 entities each of T, F and H.
  - Assert `Read` (paged and ordered), `Count` = 2 200, `Exists`, a native `Update` and a `Delete`.
  - Red on SQL Server with the budget at `int.MaxValue`.
- **Mutations:** per the plan.

Risks: above the budget the fallback runs on a snapshot of the candidates. Chunked ordering uses .NET comparison.
A best-effort native update that fails part way leaves the earlier chunks applied. All three go in the README.
