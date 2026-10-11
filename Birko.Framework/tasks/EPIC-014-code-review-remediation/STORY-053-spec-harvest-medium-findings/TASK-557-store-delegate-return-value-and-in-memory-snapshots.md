---
id: TASK-557
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-10-11
depends-on: []
blocks: []
findings: [SH-M311, SH-M315]
pr: null
github-issue: null
jira-key: null
---

# The store delegate's return value is discarded everywhere, and the in-memory store keeps the caller's live object

## Context

Spawned from [[TASK-163]] (2026-10-11). Two confirmed findings that each need an API / semantics decision rather
than a local fix, so they were not changed there.

- **SH-M315 — confirmed, broader than filed.** `public delegate T StoreDataDelegate<T>(T data)` (`IStore.cs:13`)
  returns `T`, but **every** call site — 105 across the framework, not just the in-memory ones the finding names —
  is `storeDelegate?.Invoke(data);` followed by persisting the original `data`. A delegate written in the
  functional style (returns a modified copy) is silently ignored. Options: honour the return
  (`data = storeDelegate?.Invoke(data) ?? data`, at one producer per hierarchy), or declare it `Action<T>` (breaking
  for every lambda that returns a value — consumers DraCode and Symbio implement the store interfaces).
- **SH-M311 — confirmed.** `AbstractInMemoryStore` / `AbstractAsyncInMemoryStore` store the caller's reference
  (`_items[key] = data`) and `Read` hands the same reference back, so `store.Create(e); e.Name = "x";` changes
  stored state with no `Update`. Every serializing backend takes a copy at write time, so the canonical test double
  is false-green for the whole class of missing-`Update` bugs. `AbstractModel.CopyTo` cannot be the snapshot — its
  base copies only `Guid`, and models are not required to override it. A serializer round-trip is faithful to the
  real backends but changes what `Items[guid]` / `Read` return (no longer `BeSameAs` the created object), which
  existing tests across the repo may rely on — measure that before choosing.

## Decision (2026-10-11, by the user)

- **SH-M315 — honour the delegate's return:** `data = storeDelegate?.Invoke(data) ?? data`, at one producer per
  hierarchy; `StoreDataDelegate<T>` keeps its signature (non-breaking).
- **SH-M311 — not yet decided:** measure first, as the acceptance asks — how many tests across the repo rely on
  `Read` / `Items[guid]` returning the very object that was created — then snapshot on write and read unless that
  measurement argues for a documented opt-out. The fixer reports the number and the choice in the Outcome.

SH-M315 is runnable by [[fix-next]]; SH-M311's remaining choice is a measured one, not the user's.

## Acceptance criteria

- [x] SH-M315: decision recorded; the chosen shape applied at every call site (compiler-driven if `Action<T>`); a
      test proving a returning delegate's result is what is stored (or that it no longer compiles), red-verified
- [x] SH-M311: decision recorded (snapshot on write and read, or a documented opt-out); if snapshotting, a test
      proving post-create mutation does not reach the store, red-verified; blast radius over the repo's suites
      reported as numbers
- [x] CHANGELOG entry for whichever is observable
- [x] `/specs regen store-crud-contract`

## Out of scope

- [[TASK-555]] (update of a missing entity), [[TASK-556]] (identity assignment).

## Human test plan

N/A — a library contract with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-557` — leave empty until then._

## Outcome

**Both fixed.** A store now stores whatever its store delegate returns — a delegate that hands back a modified copy used to
be ignored everywhere. And the in-memory store now behaves like a real database: it keeps its own copy, so changing an
object after saving it no longer silently changes what is stored.

- **SH-M315 — one helper, every site.** `StoreDelegates.Apply` (Birko.Data.Stores) is the one definition: the returned
  entity, the original on `null`, Guid inherited by a replacement that has none. Applied at ~110 sites in 36 files
  (single paths reassign the parameter; foreach loops over the incoming enumerable copy the iteration variable; loops
  over the list that is later written — SQL/TimescaleDB/MongoDB bulk insert, event-sourcing bulk — write the result back
  into the list). Rejected: applying the delegate once in the public base wrapper — the providers assign identity and
  check existence BEFORE the delegate (the ViewModel repositories rely on reading the new Guid in it), so it would have
  reordered that contract. **Found by the contract, not the sweep:** 10 null-checked `storeDelegate.Invoke(item)` bulk
  update loops (SQL ×8, TimescaleDB ×2). The ViewModel repositories already honoured the return through their own
  `ProcessDataDelegate` and were left as they were.
- **SH-M311 — measured, then chosen.** With copies on, the whole solution ran 168/169 projects green; the only 2 failures
  (`Create_ShouldAssignGuid_AndPersist` sync + async) asserted `BeSameAs` — the defect itself — and were inverted. So
  snapshots are on by default (`SnapshotEntities`, opt-out documented), copy = JSON round-trip (`Snapshot`, virtual), on
  write (create, replace) and on read (single, by Guid, bulk); `Items` stays the raw view.
- **Contract:** `tests/Shared/StoreContract/DelegateContract.cs` — 5 sync + 4 async, linked into all 12 provider test
  projects. Plus `InMemorySnapshotTests` (5).
- **Step-6 split:** delegate sites reverted (helper kept) → JSON 8/8, XML 24/24, SQLite 8/8 red; in-memory 8/12 red
  before the fix. Snapshot off → 5 red (3 snapshot tests + the 2 inverted). **Contract pins:** in-place delegate,
  `Only_Update_changes_the_store`, `The_opt_out_shares_references_as_before`.
- **Live (throwaway containers, removed):** PostgreSQL 145, MySQL 167, SQL Server 181, TimescaleDB 86, MongoDB 136,
  RavenDB 99, Elasticsearch 216, CosmosDB 93. Local: InMemory 131, JSON 67, XML 122, SQLite 490, ViewModel 37,
  EventSourcing 7, Patterns 85. Whole solution (no live servers): 168/169 with the 2 pinned tests red, now inverted.
- **Merge gate:** inline review. Public surface: `StoreDelegates.Apply`, `SnapshotEntities`, protected virtual
  `Snapshot`. Commit uses an explicit pathspec (shared working tree).

## Progress log

- step 2 — picked; ranked above TASK-567 (duplicate Guid on key-less SQL tables, needs a DDL decision) on key 4: decided 2026-10-11 (SH-M315 honour the return; SH-M311 measured choice) and self-contained, while TASK-567 opens with a schema/migration decision
- step 3 — verified: held as written (SH-M315: every site discards; SH-M311: live reference stored and returned)
- step 4 — layer: local; SH-M315 per user decision; SH-M311 choice measured (168/169 green with snapshots) → snapshot on, opt-out
- step 5 — `StoreDelegates.Apply` at ~110 sites (36 files + 10 null-checked loops found by the contract); in-memory snapshots; `DelegateContract` (9) on 12 providers; `InMemorySnapshotTests` (5); 2 BeSameAs tests inverted; all suites green
- step 6 — reverted delegate sites → contract red on JSON/XML/SQLite; snapshot off → 5 red
- step 7 — respecced store-crud-contract: delegate requirement replaced (*A store persists what the store data delegate returns*, 2 scenarios inverted); in-memory requirement gained *The store keeps copies, like a real backend*
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
