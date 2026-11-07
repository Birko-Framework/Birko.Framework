---
id: TASK-841
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-839]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB chunking splits string lists too, since Cosmos compares strings exactly

## Context

`CosmosInListChunking` (TASK-834, reused by TASK-839) refuses a string list past the query-size limit. That rule
comes from TASK-833's SQL decision: under a case- or accent-insensitive collation, two distinct C# strings can be one
database value, and a row could match two chunks.
- Cosmos DB has no collation. `IN` and `ARRAY_CONTAINS` compare strings exactly, and TASK-839 measured that `"N2"`
  does not match `n2`.
- Distinct C# strings are therefore distinct Cosmos DB values, and string chunks are disjoint.
- The refusal blocks lists that would work, in the base store and in views alike.

## Acceptance criteria

- [x] Confirmed on the live emulator first: a string `IN` / `ARRAY_CONTAINS` matches only the exact string, for case,
      a trailing space and accents. If any of these matches a different string, stop and record it; the refusal then
      stays.
      → Gate passed (vnext-preview, 2026-11-07). Both `IN`, from the base store's own LINQ, and
        `ARRAY_CONTAINS(@p, path)` matched only the exact string for:
        - `"a"` / `"A"`;
        - `"a"` / `"a "`;
        - U+00E9 / `e` + U+0301;
        - `"Straße"` / `"STRASSE"` / `"strasse"`.
      → The gate is a permanent test (rule 54). The refusal returns if the premise ever stops holding.
- [x] `CosmosInListChunking` splits string lists. The SQL planner (`InListChunkPlanner`) keeps its refusal; the rule is
      per provider, stated in each.
      → Only the string refusal is removed; every other rule stays. Deduplication is ordinal, which is default string
        equality.
      → The per-provider rule is stated in the planner's remarks, `Birko.Data.CosmosDB/CLAUDE.md`, the Views
        `CLAUDE.md` and the `CosmosViewStore` class doc. The last two still said "refused" and were corrected at close.
      → The SQL planner is untouched.
- [x] Live tests: a string list past a lowered limit equals the C# oracle, for read and count, in the base store and
      in a view.
      → `Task841StringChunkingTests` (the gate, offline ordinal dedupe, live read and count, sync and async) and
        `Task841ViewStringChunkingTests`: chunks of 3 under a 2,000-character limit that the whole list (about 2,860
        characters) exceeds.
      → The "string" refusal cases in TASK-834's and TASK-839's tests are removed.
      → Red proof: with the old refusal back, 3 tests failed; the gate test stayed green.
      → Live suites green on the combined tree with TASK-840: CosmosDB 192, CosmosDB.Views 90.
      → **Found, filed:** 1000 long strings can overflow one chunk and are refused rather than split smaller →
        TASK-842.

## Out of scope

- The SQL providers' string refusal (TASK-833, the user's decision).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
