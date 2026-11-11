---
id: TASK-920
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce SUM / AVG / MIN / MAX nested in filters on both plan paths (rule 54); check real Azure if possible.
- [ ] Rewrite to an aggregate-free form that answers as C# on both paths, or refuse by name; tests red before, run on
      the gateway path too.
- [ ] Decide how the gateway path is covered (CI already runs Linux; a documented local switch).

## Out of scope

- `Count(p)` (TASK-919, done).

## Human test plan

N/A — fully covered by automated tests.
