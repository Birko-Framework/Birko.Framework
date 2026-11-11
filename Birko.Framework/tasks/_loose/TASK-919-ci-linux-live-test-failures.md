---
id: TASK-919
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-907, TASK-899]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Live tests failing on CI (Linux) after the 2026-11-11 push

## Context

The push of 5dbafe81 (CI run 43044564590) failed live-tests on Linux while every suite passed on Windows: the SQL
case-fold contract (PostgreSQL 6, MySQL 4, SQL Server 4), Cosmos TASK-907's mixed-astral refusal (4) and TASK-899's
nested-predicate tests (2). helper-scripts failed separately on stale RavenDB spec-links (fixed in 16f7c169).

## Acceptance criteria

- [x] Reproduce each on Linux .NET and on both Cosmos emulator builds (rule 54).
      → Measured 2026-11-11 in an sdk:10.0 container and against emulator builds EN20261008 and EN20261019.
        (A) The CI runner is invariant culture; the contract's expected answer was `ToLower()`/`ToUpper()` in the current
        culture, measured on Windows under sk-SK (`"İstanbul".ToLower()`). A probe of all 34 fold texts gave identical
        results on Windows and Linux per culture (not ICU vs NLS); the Linux container reproduced CI's 6/4/4 and passed
        12/12 with `LANG=sk_SK.UTF-8`. Production unchanged (server folds are LOWER/UPPER per TASK-889).
        (B) Not the emulator version: Cosmos SDK 3.64.0 builds query plans locally (ServiceInterop) only on Windows x64;
        Linux asks the gateway. Windows forced onto the gateway path (`AZURE_COSMOS_BYPASS_QUERY_PARSING=true`) failed the
        same 6 on both builds. Defect 1: TASK-907's regex wrote astral ranges as escaped surrogate pairs, which the
        gateway path does not read as one character — the mixed-group check counted 0 and a mixed MIN/MAX group was
        answered in code-point order instead of refused. Defect 2: TASK-899's nested `Items.Count(p)` renders as
        `JOIN (SELECT VALUE COUNT(1) ...)`, which the gateway plan takes as the query's own aggregate, so reads threw.
- [x] Fix so C# semantics hold on both OSes, tests red before.
      → (A) test only: the three fold tests pin `CurrentCulture` to sk-SK (`FoldCulture`, `CultureScope`); nothing
        loosened. (B) `CosmosAggregationHelper.Matches` writes the characters themselves (only control characters,
        quotes, backslashes escaped); new `CosmosFilterRewrite.CountOfElements` rewrites `Count(p)`, `LongCount(p)` and
        `Where(..).Count()` to `Count(Where(src, p).ToArray())` → `ARRAY_LENGTH(ARRAY(SELECT VALUE ..))`, no aggregate,
        TASK-899's null semantics kept. `GatewayQueryPlanRenderingTests` (9 offline) red 7/9 with the production hunks
        reversed, green 9/9.
      → After: Linux SQL contracts 54/54 per provider; Cosmos Linux new emulator CosmosDB 597, Views 173; Windows gateway
        path both builds 597/173; Windows normal path all seven Cosmos suites green; Windows SQL suites green.
      → Staged HEAD + patch snapshot (Windows, live): PostgreSQL 2602, MySQL 2624, SQL Server 2658, SQLite 3182,
        CosmosDB.Views 173, CosmosDB 597, CosmosDB on the gateway path (aggregate, Task899, Gateway tests) 136 - all green.
      → **Found, filed:** TASK-920 (P1: nested SUM/AVG/MAX in a Cosmos filter on the gateway-plan path).

## Out of scope

- Nested SUM/AVG/MAX (TASK-920).

## Human test plan

N/A — fully covered by automated tests.
