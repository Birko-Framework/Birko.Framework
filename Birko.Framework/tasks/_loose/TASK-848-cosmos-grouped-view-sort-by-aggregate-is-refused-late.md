---
id: TASK-848
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-844]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A Cosmos DB grouped view sorted by an aggregate is sent to the server, which refuses it

## Context

Found by TASK-844 (2026-11-07), measured on the emulator. Cosmos DB cannot sort a grouped view by an aggregate
property in any form:
- a bare alias gives SC2001;
- a quoted alias, or `ORDER BY SUM(...)`, gives 2206.

`CosmosViewStore` builds and sends the statement anyway, and the server answers 400. The failure is loud and predates
TASK-844. `CosmosViewAggregateSqlTests` pins the text of a statement that cannot run.

## Acceptance criteria

- [x] Re-measured: each form the store can render for "sort a grouped view by an aggregate" fails on the emulator.
      → On the emulator, 2026-11-07, these fail with a 400:
        - `ORDER BY Total` (SC2001);
        - `ORDER BY "Total"` and `ORDER BY SUM(c["Value"])` (2206);
        - the same for `COUNT`, and for an aggregate as a second key after a group key.
      → What works: sorting by a group key, number or string, including `ORDER BY key DESC OFFSET 1 LIMIT 2`. Grouped
        queries already page on the server.
- [x] Either the sort is applied correctly another way (for example over the grouped rows in memory, if that keeps
      paging correct) or it is refused by name with `NotSupportedException` before any request. The choice is
      recorded here, with its reason.
      → **Refused.** Sorting in memory would drop server paging and read every group before taking a page, an unbounded
        read behind a paged call. It would also need a second ordering implementation matching Cosmos DB's ordering
        across types.
      → `ResolveOrderKey` throws, naming `View.Property` and TASK-848. Every grouped path builds the statement before
        sending anything. The reason is also in the code remark and the project's `CLAUDE.md`.
- [x] The test that pins the unrunnable statement is replaced by one that pins the chosen behaviour; red before the
      fix.
      → `CosmosViewAggregateSqlTests`:
        - pins `ORDER BY c["Category"] DESC` for a group-key sort;
        - pins the refusal before any request (offline), with the aggregate as the only key and after a group key.
      → `Task848CosmosViewAggregateSortTests` (live): a group-key sort with paging equals the oracle; sorting by `Sum`
        or `Count` is refused.
      → Red proof: with the old branch back, the 4 refusal tests failed (`CosmosException` live, `HttpRequestException`
        offline); the group-key tests passed.
      → Suites live: CosmosDB.Views 108, CosmosDB 200.

## Out of scope

- Reserved-word aliases (TASK-844, done).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
