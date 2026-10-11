---
id: TASK-557
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
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

- [ ] SH-M315: decision recorded; the chosen shape applied at every call site (compiler-driven if `Action<T>`); a
      test proving a returning delegate's result is what is stored (or that it no longer compiles), red-verified
- [ ] SH-M311: decision recorded (snapshot on write and read, or a documented opt-out); if snapshotting, a test
      proving post-create mutation does not reach the store, red-verified; blast radius over the repo's suites
      reported as numbers
- [ ] CHANGELOG entry for whichever is observable
- [ ] `/specs regen store-crud-contract`

## Out of scope

- [[TASK-555]] (update of a missing entity), [[TASK-556]] (identity assignment).

## Human test plan

N/A — a library contract with no UI surface; fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-557` — leave empty until then._
