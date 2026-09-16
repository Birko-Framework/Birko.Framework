---
id: TASK-446
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# All seven workflow instance stores have zero store-level tests, and that is where two high findings lived

## Context

Spawned by [[TASK-315]]'s close gate (`/tasks close` step 5d) from a "flagged, not fixed" note in its
Outcome. Measured 2026-09-16 while fixing `SH-H056` + `SH-H057`:

- All seven backend test projects — `Birko.Workflow.{SQL,JSON,XML,MongoDB,RavenDB,ElasticSearch,CosmosDB}.Tests`
  — contain exactly **one file each**, and every one of them tests only the **model**
  (`*WorkflowInstanceModelTests.cs`: `FromInstance` / `ToInstance` / `UpdateFromInstance`).
- **Nothing tested a store.** Not `SaveAsync`, not `LoadAsync`, not `DeleteAsync`, not any `Find*`.
- `IWorkflowInstanceStore` has **0** callers in the framework (`WorkflowEngine` never touches it), **0**
  construction sites in any test, and **0** `.cs` files across all 16 consumer repos.

That is not a coincidence with TASK-315: it is the reason both findings survived. `SH-H057` — every
backend relabelling and overwriting another workflow's row — would have been caught by any two-workflow
round-trip test, and the whole eight-suite run stayed green through the entire fix until the 12 new
tests were written.

TASK-315 left this behind deliberately rather than widening: its `## Out of scope` records that test
coverage was excluded from the spec-harvest sweep, so a missing test was not a finding there. It *is*
work, it names no owner, and 5d's rule is that such a bullet gets an id rather than a paragraph.

**What TASK-315 already added**, so this task starts from it rather than repeating it:

- `Birko.Workflow.Tests/WorkflowInstanceOwnershipTests.cs` — the shared guard, plus a **cross-backend
  source scan** that reads all seven store sources and fails when one drops the ownership guard or the
  workflow scope. That scan exists precisely because six of the seven cannot be exercised offline.
- `Birko.Workflow.JSON.Tests/JsonWorkflowInstanceStoreOwnershipTests.cs` — the one end-to-end store
  suite, on the offline-reachable backend, asserting observed row state.

## Acceptance criteria

- [ ] Each of the seven backends has a store-level suite covering the `IWorkflowInstanceStore` contract
      end to end — `SaveAsync` insert **and** update, `LoadAsync` hit and miss, `DeleteAsync` hit and
      miss, and all three `Find*` queries including their `limit` and `UpdatedAt`-descending ordering
- [ ] The backends needing a live server (MongoDB, RavenDB, ElasticSearch, CosmosDB) follow this repo's
      existing live-suite convention — gated, and **`BIRKO_REQUIRE_LIVE` promotes a skip to a failure**.
      ⚠ CLAUDE.md § TASK-259/266 record that setting that variable across a suite whose server is down
      reads as signal when it is not; stand the container up rather than reading the skips
- [ ] ⚠ **Before accepting that a backend needs a live server, try rendering its query offline.**
      § TASK-309 measured that CosmosDB renders query SQL with no account and no network
      (`ToQueryDefinition().QueryText` off a container built from an unreachable connection string), and
      § TASK-218 that RavenDB builds RQL from an uninitialised `DocumentStore`. A suite that skips
      offline when it did not have to is a suite that never runs
- [ ] The two-workflow case is covered on **every** backend, not only JSON: two workflows with different
      `TData` sharing one table/collection, asserting that `FindByState`/`FindByStatus` return only the
      named workflow's rows and that a cross-workflow `SaveAsync` is refused with the foreign row intact
- [ ] ⚠ Each backend's **filter translation** is asserted, not assumed. TASK-315's scoped predicate is
      `m.WorkflowName == workflowName && m.CurrentState == state`, handed to each driver's own
      translator; § Conventions records several cases where a driver silently widened or dropped a
      conjunct (MongoDB's `$nin: []`, RavenDB emitting no `where`, Cosmos's `root["x"] = null`). The
      assertion is the rows returned, never that no exception was thrown
- [ ] Where a new suite makes TASK-315's source scan redundant for a given backend, the scan is **kept**
      and the reason recorded — it is the only check that fires when a *new* backend is added

## Out of scope

- Changing `IWorkflowInstanceStore` or any backend's behaviour. This is coverage for the contract as it
  now stands after [[TASK-315]]. A defect the new tests find is a fresh finding, filed separately.
- `SaveAsync`'s non-atomic read-then-write upsert and its documented per-backend race characteristics.
  That is specced, deliberate, and a concurrency-hardening task if anyone wants it — not a test gap.
- The other `Birko.Workflow.*` surfaces (`WorkflowEngine`, the builders, the diagram generators), which
  do have tests in `Birko.Workflow.Tests`.

## Human test plan

_Resolve before `/tasks close` — expected `N/A`, but the live-suite backends may need a documented
container bring-up step._

## Implementation plan

_Populated by `/tasks plan TASK-446` — leave empty until then._
