---
id: TASK-849
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-833]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The IN-list chunk planner decides "this update writes the filtered column" by comparing bare names

## Context

Flagged by the bulk-filter-operations respec (2026-11-07), from reading the code; not reproduced. The user chose to
file it.
- TASK-833's review found that a chunked UPDATE writing the filtered column applies twice: a row moved into a later
  chunk's values matches again. The fix never splits such an update.
- The planner identifies the written columns by text: `WrittenColumns` / `BareColumn` split each SET entry on `=` and
  compare bare names.
- A SET entry whose target that split does not isolate escapes the check, and the update is chunked. Examples: a
  qualified or quoted name in a form the comparison does not normalise, or a computed SET from TASK-826.
- The result would be the double increment again, silently.

## Acceptance criteria

- [ ] Probe every SET form the update paths can render (plain, `PropertyUpdate` Set / Increment / Compute, expression
      values, qualified and quoted names, each provider's quoting) for one whose written column the check misses.
      Record the result.
      → **Reproduced (2026-11-07, SQLite connector at limit 40, chunk 10). The escape is on the FILTER side.** When the
        IN operand is a TASK-213 value fragment, `BareColumn` returned text like `Score + 0)`, so the planner missed
        the written column and split the update, which then double-applied.
      → Forms that escaped:
        - `Contains(x.Score + 0)`, `x.Score * 1`, a CASE over the column, a two-column fragment;
        - Set+Increment, `Compute`, the sync store, and both connector `Update<T,P>` expression overloads.
      → Did not escape: a plain column, a cast column, and the SET side in any form. Provider quoting is irrelevant:
        condition names and SET columns are bare metadata names (rule 17) before rendering.
- [x] The written columns come from the update's own field list (the `PropertyUpdate` assignments or the fields
      dictionary), not from parsing rendered SQL text. One producer, shared with the statement builder (rule 16).
      → New `SetClauses` (`Dictionary<int,string>`) is the only builder of `col = expr` clauses and records the column
        each one writes. `PropertyUpdateSqlTranslator` and both connector expression overloads use it, and
        `SetClauses.WrittenBy` gives the planner the written columns. A plain dictionary of already-rendered clauses
        has no known columns, so it is never split (refused past the limit).
      → Filter side: `InListChunkPlanner.Identifiers` lists every identifier the operand reads, skipping literals and
        parameters. A list whose operand reads any written column is never split, which errs on the side of not
        splitting.
      → `PlanInListChunks` takes `updateFields` / `isExpressionValues`. `WrittenColumns` / `BareColumn` are deleted.
- [x] Regression test: each form that writes the filtered column is never chunked, and is refused past the limit.
      Red before the fix if a form escaped.
      → `Task849WrittenColumnChunkingTests` (32 tests): 14 fail with the old bare-name check; TASK-833's tests stay
        green.
      → All 35 Birko.Data.SQL importer suites green against live PostgreSQL, MySQL and SQL Server.
      → **Found, filed:**
        - On SQLite, a decimal value fragment in IN or `=` matches nothing, silently → TASK-856 (P2).
        - `PropertyUpdate` on a column renamed with `HasColumnName` writes the C# name and fails loudly → TASK-855.

## Out of scope

- The chunking rules themselves (TASK-833).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
