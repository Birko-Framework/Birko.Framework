---
id: TASK-844
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A Cosmos DB view property named like a reserved word fails every query

## Context

Found by TASK-840 (2026-11-07) on the emulator. `CosmosViewStore` writes the projection alias unquoted
(`c["Sub"] AS Inner`), so a view property named like a Cosmos SQL reserved word (`Inner`, `Value`, …) makes every query
on that view fail with a 400 syntax error. The failure is loud.

## Acceptance criteria

- [x] Reproduced on the emulator with a view property `Inner` and one named `Value`.
      → On vnext-preview, 2026-11-07, each of these failed with 400 SC1001 "incorrect syntax near":
        - `SELECT c["Sub"] AS Inner`
        - `c["Value"] AS Value`
        - `WHERE c.Value = 1`, and `WHERE c.Sub.Value = 11`
        - `ORDER BY c.Value`
      → So a dotted filter or sort path on a keyword-named field fails too, not only the alias.
- [x] The alias is written so any property name works, for example quoted the way Cosmos SQL allows, or a generated
      alias mapped back to the property. Reads and counts on such a view equal the C# oracle.
      → **What Cosmos accepts, measured:**
        - `AS "Value"` works, and `AS "we\"ird"` keeps the exact name.
        - `AS [Value]` and backticks are syntax errors.
        - `c["Value"]` and `c["Sub"]["Value"]` work in WHERE and ORDER BY.
      → `CosmosAggregationHelper.Alias` is the one producer of an alias: a quoted string, escaped like `FieldRef`. The
        view projection and the shared group-by and aggregate aliases use it, so store-level aggregation gets it too.
      → `DocumentPath` (WHERE, `Contains`) and `ResolveOrderKey` build bracket-quoted paths through `PathRef`.
      → The SQL-text assertions in 6 existing test files were updated for the quoted form, including the shared
        helper's `CosmosAggregationHelperTests`.
- [x] A regression test red before the fix.
      → `Task844CosmosViewReservedNameTests` (7 tests against the C# oracle): reads, counts, `QueryFirst`, filters, a
        sort with paging, and a grouped view.
      → Red proof: on HEAD all 6 that existed then failed (SC1001). With only the alias fix, 3 still failed near
        `Value`, which shows the path quoting is needed on its own.
      → Suites live: CosmosDB.Views 103, CosmosDB 200.
      → **Found, filed:** Cosmos DB cannot sort a grouped view by an aggregate in any form (SC2001 or 2206, measured).
        It fails loudly, and did on HEAD too, and `CosmosViewAggregateSqlTests` pins a statement that cannot run →
        TASK-848.

## Out of scope

- Projections of nested members (TASK-843).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
