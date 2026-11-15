---
id: TASK-920
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-919]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB: nested SUM / AVG / MAX in a filter break on the gateway query-plan path

## Context

Found by TASK-919 (2026-11-11, emulator, client forced onto the gateway query plan as on Linux): a filter holding a
nested aggregate over a member collection — SUM throws "localSum must be a number", AVG throws, and the MAX form
(`ARRAY(SELECT VALUE MAX(..))`) **silently returned 1 of 6 matching documents**. Windows x64 builds the plan locally
(ServiceInterop) and answers all three correctly, so Windows runs never see it; Linux deployments use the gateway path.
Real Azure's gateway plan probably matches ServiceInterop (may be emulator-only) — unverified. TASK-919 fixed the same
family for `Count(p)` by rewriting to `ARRAY_LENGTH(ARRAY(SELECT VALUE ..))`.

Also from TASK-919: Windows developers never exercise the gateway path (`AZURE_COSMOS_BYPASS_QUERY_PARSING=true` forces
it); `Microsoft.Azure.Cosmos` floats at `3.*` in `Birko.Packages.props`.

## Acceptance criteria

- [x] Reproduce SUM / AVG / MIN / MAX nested in filters on both plan paths (rule 54); check real Azure if possible.
      → Measured 2026-11-15, emulator vnext-preview EN20261019, 12 shapes × read / sync read / count / sync count /
        aggregate / update / delete, C# over the documents as read back as the oracle. **Two defects, not one:**
        (A) **Both plan paths, silently wrong:** Cosmos aggregates a null element where C# skips it — SUM / AVG of
        [10, null] are undefined, MIN is null (null orders below numbers). 4 of 12 shapes wrong on the LOCAL (Windows)
        plan in every path, e.g. `Sum(Score) > A` missed c1 (C# 10 > 1), `Min(Score) != null` missed c1,
        `Average(Score) == null` kept c1. Not in the task as filed.
        (B) **Gateway plan:** every nested aggregate throws on read / update / delete ("RewrittenAggregateProjections
        was not an array"), and the count path, which does not throw, miscounted by (A).
        Experiment: the same shapes with an explicit `Where(Score != null)` answered as C# on the local plan.
        Real Azure: not measured (no account in reach); the CI emulator (vnext-preview) is a PostgreSQL-based
        reimplementation, so its gateway planner is the likely culprit, unverified.
- [x] Rewrite to an aggregate-free form that answers as C# on both paths, or refuse by name; tests red before, run on
      the gateway path too.
      → **The user's decision (2026-11-15): fix the nulls, keep the query** (over "refuse everywhere by name" and
        "measure on real Azure first"). There is no aggregate-free form: Cosmos has no array sum / average / min / max
        function (Count had ARRAY_LENGTH). Where the gateway cannot plan the subquery it keeps failing loudly, never
        answering wrong.
      → `CosmosFilterRewrite`: a nested Sum / Average / Min / Max over a value that can be null aggregates
        `Where(v != null)`, as C# does; the element type comes from the sequence (`Sum(IEnumerable<int?>)` is not
        generic); preparing twice does not filter twice. `ElementType` now has one producer (CountOfElements uses it).
      → Live `Nested_aggregates_answer_as_CSharp` (+ `_in_writes`), 17 shapes: before, 24 wrong answers on the local
        plan and throws + wrong counts on the gateway plan; after, all answer as C# on the local plan, and on the
        gateway plan every path either answers as C# or throws (74 accepted loud), none wrong. Offline
        `GatewayQueryPlanRenderingTests` +8 (17/17); with the fix switched off 6 fail.
- [x] Decide how the gateway path is covered (CI already runs Linux; a documented local switch).
      → CI's Linux run is the gateway path; the live matrix takes `loudOnGatewayPlan` (an exception is accepted
        there, a wrong answer never is). Locally: `AZURE_COSMOS_BYPASS_QUERY_PARSING=true` (in the test's doc comment).

## Out of scope

- `Count(p)` (TASK-919, done).

## Human test plan

N/A — fully covered by automated tests.

## Closed (2026-11-15)

Regression, emulator EN20261019: every importer of Birko.Data.CosmosDB on the local (Windows) plan — CosmosDB 607,
Views 173, ViewModel 5, Migrations 44, Sync 4, BackgroundJobs 28, Workflow 8, all green; CosmosDB 607 on the gateway
plan (`AZURE_COSMOS_BYPASS_QUERY_PARSING=true`), green. Real Azure's gateway plan: still unmeasured.
