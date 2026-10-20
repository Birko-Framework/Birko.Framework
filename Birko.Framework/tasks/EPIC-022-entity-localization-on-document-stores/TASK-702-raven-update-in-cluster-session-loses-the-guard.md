---
id: TASK-702
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-20
depends-on: []
blocks: [TASK-700]
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB: an update inside a unit of work fails on a document a cluster transaction wrote

## Context

Spawned 2026-10-20 from [[TASK-700]], measured live on RavenDB 7.2 while testing its RavenDB write boundary.

`AsyncRavenDBStore` / `RavenDBStore` update a document by loading it, evicting it from the session and storing the
caller's object under the same id. `RavenDbUnitOfWork` opens a cluster-wide session, and a document written by a
cluster transaction carries an atomic guard (`rvn-atomic/<id>`). The evicted object takes the guard's index with it, so
the stored replacement saves with index 0 and the commit fails:

- update in a unit of work of a document a unit of work created: `ClusterTransactionConcurrencyException`
  ("Guard compare exchange value ... index does not match the transaction index's 0");
- the same update of a document a plain create wrote: works.

So any document written through a `RavenDbUnitOfWork` cannot be updated through one again. TASK-700's boundary writes
every document in a cluster transaction, so every later localized update through it would fail.

## Acceptance criteria

- [x] An update (single and collection, async and sync) inside a `RavenDbUnitOfWork` of a document a cluster transaction
      wrote commits, live.
- [x] Outside a unit of work, an update of such a document still works.
- [x] The fix keeps the session's tracked document instead of evicting it.

## Out of scope

- `UpdateIfAsync`, which refuses to run inside a unit of work.

## Human test plan

N/A — asserted by the live suite.

## Implementation plan

1. Copy the caller's values onto the document the session loaded and tracks (`RavenTrackedCopy.Into`), instead of
   evicting it and storing the caller's object, on all four update paths. The tracked document keeps the guard index
   the load brought.
2. Live tests in Birko.Data.RavenDB.Tests for the four paths in and out of a unit of work.

## Outcome

Done 2026-10-20. All four update paths (async and sync, single and collection) copy the caller's values onto the
document the session loaded (`RavenTrackedCopy.Into`): public fields, and public properties with a setter of any
visibility, which is what RavenDB stores. The tracked document keeps the guard index its load brought. An entity of
another runtime type than the loaded document is refused rather than half copied.

**Measured live (RavenDB 7.2):**
- before: an update in a unit of work of a document a unit of work created failed with
  `ClusterTransactionConcurrencyException`; of a document a plain create wrote, it worked;
- after: single and collection updates in a unit of work commit, on the async and the sync store;
- a plain update of such a document still works, and a unit of work after it too;
- an entity read in the same session and edited in place updates.

**Tests:** `Task702ClusterUpdateLiveTests` (6: five live, one refusing an entity of another runtime type).

**Mutations, all red:** fix reverted (5 red); fields not copied (1 red); private setters not copied (1 red).

**Regression:** Birko.Data.RavenDB.Tests 128/128, and the six other suites that import Birko.Data.RavenDB.
