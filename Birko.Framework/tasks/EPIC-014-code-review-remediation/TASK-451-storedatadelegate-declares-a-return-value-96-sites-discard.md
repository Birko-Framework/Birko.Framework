---
id: TASK-451
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-17
depends-on: []
blocks: []
related: [TASK-316]
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H035]
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Stores, Birko.Data.SQL, Birko.Data.InMemory, Birko.Data.JSON, Birko.Data.XML, Birko.Data.ElasticSearch, Birko.Data.MongoDB, Birko.Data.CosmosDB, Birko.Data.InfluxDB, Birko.Data.RavenDB, Birko.Data.EventSourcing]
---

# `StoreDataDelegate<T>` declares a return value that 96 call sites discard

## Context

Spawned by [[TASK-316]], which fixed `SH-H035`'s **repository** half and deliberately left this half
rather than widening into a framework-wide contract change. Not a restatement: the repository no longer
*relies* on the mechanism, so nothing is broken today — what remains is a public contract that promises
something no implementation delivers.

`Birko.Data.Stores/IStore.cs:13` declares:

```csharp
public delegate T StoreDataDelegate<T>(T data) where T : Models.AbstractModel;
```

A delegate returning `T` reads as a **transform**: return a replacement instance to have it persisted,
and (as `AbstractViewModelRepository` assumed until TASK-316) return `null` to suppress the write.

## Measured, 2026-09-17

```
storeDelegate?.Invoke(...) / storeDelegate.Invoke(...)   96 sites   (framework, non-test)
sites whose result is assigned or returned                0
```

Every backend calls it for side effects and persists its own `data` regardless — `DataBaseStore`,
`AbstractInMemoryStore`, `AbstractJsonStore`, `AbstractXmlStore`, `ElasticSearchStore`, `MongoDB`,
`CosmosDB`, `InfluxDB`, `RavenDB` and the two `EventSourcing*BulkStoreWrapper`s, sync and async alike.

So the signature is the same silent-drop shape § Conventions records for `CreateAbstractField`'s
`return null` (SH-H037): a contract that cannot be honoured, failing quietly rather than refusing.

## The decision to take first

Two coherent answers, and the choice is the work:

1. **Honour it** — every site becomes `data = storeDelegate?.Invoke(data) ?? data;`. Makes the
   declaration true and gives every backend the transform semantics the bulk ViewModel path already has
   (CR-H110). 96 edits, and it *changes behaviour* for any consumer whose delegate currently returns
   something other than what it was handed.
2. **Narrow the declaration to `void`** — i.e. say out loud that it is a side-effect hook. One edit at
   the declaration, loud breakage (`CS`-level) at every consumer delegate that returns a value, and it
   forecloses the transform capability.

⚠ **Do not split the difference.** A contract honoured by some backends and not others is worse than
either answer — that is § TASK-274's *two doors onto one feature must give one answer*, and this has 96
doors.

## Acceptance criteria

- [ ] Consumer reach is **measured** across all 16 consumer repos before choosing — how many `.cs` files
      pass a `StoreDataDelegate` at all, and how many of those return anything but their argument. The
      answer decides whether option 2 is affordable
- [ ] One answer is chosen and applied to **every** site, with the reasoning recorded; no backend is left
      on the other behaviour
- [ ] If option 1: a test per store family asserts a delegate returning a **replacement instance** has
      that instance persisted — asserted as the **value read back**, never as the absence of an exception
- [ ] If option 2: the declaration change is shown to break loudly, and the transform capability's
      removal is recorded where a consumer will meet it
- [ ] [[TASK-316]]'s repository-level fix keeps working either way, and its
      `ViewModelUpdateMergeTests` stay green — the repository applies the transform itself now, so option
      1 must not cause it to be applied twice

## Out of scope

- The ViewModel repositories' own use of the mechanism — [[TASK-316]] closed that, and the repository is
  now independent of whichever answer this task picks.

## Human test plan

`N/A — fully covered by automated tests` (a library contract with no UI surface).
