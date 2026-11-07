---
id: TASK-839
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-837]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB view filters support `Contains`, as one array parameter

## Context

TASK-837 found that `CosmosViewStore` renders its own SQL through `CosmosFilterTranslator`, not LINQ:
- `v.Tags.Contains(x)` rendered `CONTAINS()`, a substring function, over an array and matched nothing, silently.
- Since TASK-837, every collection `Contains` in a view filter is refused before any request.

The user asked for the capability (2026-11-07). The base Cosmos DB store, which uses LINQ, already supports both
forms.

**Two forms:**
- **A list of values against a view property:** `ids.Contains(v.Key)`, the IN shape. Rendered as
  `ARRAY_CONTAINS(@p, <path>)` with the whole list bound as **one** array parameter. The query text stays the same
  size whatever the list length, so it needs no chunking: the 524,288-character query-text ceiling (TASK-834) does not
  apply.
- **A view array property containing a value:** `v.Tags.Contains(x)`. Rendered as `ARRAY_CONTAINS(<path>, @p)`.

`ARRAY_CONTAINS` compares exactly, with no collation, so string lists are safe, unlike the SQL providers'
(TASK-833). `!` around either form gives `NOT ARRAY_CONTAINS(...)`. An empty list matches nothing (rule 42: never "no
constraint").

## Acceptance criteria

- [x] `ids.Contains(v.Key)` and `v.Tags.Contains(x)` in a Cosmos view filter return exactly the C# oracle's rows on
      the live emulator, for read and count, with Guid, int and string values, a negated form, a form under `||`, and
      an empty list (no rows).
      → `CosmosFilterTranslator`:
        - array spans are normalised with `SpanContains.Rewrite` (rule 47);
        - `ids.Contains(v.Key)` becomes `ARRAY_CONTAINS(@p, c.Key)`, the list bound as one `object[]` (enums as
          numbers);
        - `v.Tags.Contains(x)` becomes `ARRAY_CONTAINS(c.Tags, @p)`;
        - an empty list renders `false`.
      → `Task839CosmosViewContainsTests` (31 tests, live). TASK-837's refusal theories for these two shapes are
        removed, since the shapes are now supported.
- [x] The list is bound as one parameter. A list whose serialized size would pass the limit is split, as TASK-834
      does for the base store: a read or count whose `Contains` is a plain AND conjunct is chunked and the results
      combined. Negated or `||` forms past the limit refuse by name before any request. A read with at least 20,000
      ids succeeds through chunks.
      → **Reworded 2026-11-07, before any code, on measurement:**
        - "One parameter escapes the text ceiling" is false. The emulator counts the serialized parameter values
          toward the same 524,288-character limit, so 13,795 Guids pass and 25,000 get 400 SC3020. 65,530
          seven-digit ints pass. 100,000 extra characters of query text cut the Guid maximum to 11,163.
        - `ARRAY_CONTAINS` with an array parameter works for Guid, int and string values (exact string match), and
          with `NOT`, an empty list and count.
        - The user chose to split, like TASK-834.
        - The probe is in the session scratchpad (`Task839Probe.cs.txt`).
      → **As built:** the planner is TASK-834's `CosmosInListChunking.Plan`, unchanged (one producer, rule 16).
        - The size it checks is the query text plus each parameter as JSON.
        - Reads concatenate the chunks and counts are summed.
        - `InListChunkSize` and `MaxQueryTextLength` are `protected virtual`.
        - A grouped view is never split, because a group can span chunks; past the limit it refuses.
      → A 20,000-id read and count pass through chunks.
      → Red proof: with the planner off, 7 tests failed; the 20,000-id read got a live 400 SC3020, and 6 refusals past
        the limit reached the network.
      → **Deviation, now TASK-841:** the shared planner refuses a string list past the limit, a rule written for SQL
        collations. `ARRAY_CONTAINS` is exact, so for Cosmos DB that refusal is over-cautious. Below the limit, string
        lists run as one query.
- [x] The view property path goes through the existing `MapViewPropertyToSource`; a property that does not map refuses
      by name.
- [x] Shapes that stay unsupported refuse by name before any request, as TASK-837 does. Examples: `Contains` on a
      computed expression, or a nested collection.
      → Refused by name: a comparer overload, a computed operand, a nested member (`v.Inner.Tags`, see TASK-840), a
        collection of collections, and a test where both sides read the view.
      → The empty-list rule was disabled and run red (the translator test). An empty bound array also returns 0 rows
        live, so the live tests cannot tell.
- [x] `Birko.Data.CosmosDB.Views/CLAUDE.md` and the class doc describe what is supported.
      → Both updated. Suites green live: CosmosDB.Views 72, CosmosDB 192.

## Out of scope

- A nested view property `v.Inner.X` renders as `c.X` → TASK-840.
- Moving the base Cosmos DB store from LINQ to array parameters. TASK-834 chunks it, which works; any change there is
  a separate decision.

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan

1. `CosmosFilterTranslator`: recognise the two `Contains` shapes (an `Enumerable.Contains` or instance `Contains` on
   an `IEnumerable` that is not a string). Bind a constant list as one parameter, and render the path through
   `MapViewPropertyToSource`. Everything else keeps TASK-837's refusal.
2. Tests in `tests/Birko.Data.CosmosDB.Views.Tests`, live, against the oracle. Replace TASK-837's refusal tests for
   these two shapes; keep the ones for shapes that stay unsupported.
3. Docs: the project `CLAUDE.md` and the class doc.
