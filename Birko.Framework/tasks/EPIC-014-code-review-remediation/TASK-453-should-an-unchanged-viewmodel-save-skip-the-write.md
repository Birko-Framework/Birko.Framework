---
id: TASK-453
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-09-17
depends-on: []
blocks: []
related: [TASK-316, TASK-451]
findings: [SH-H035]
pr: null
affects: [Birko.Data.ViewModel, Birko.Data.Patterns, Birko.Data.EventSourcing]
---

# Decide whether an unchanged ViewModel save should skip the write

## Context

The other half of `SH-H035`, deliberately left by [[TASK-316]] rather than guessed at.

`AbstractViewModelRepository` maintains a SHA-256 hash per entity and used to express *"nothing changed,
skip this write"* by returning `null!` from the `StoreDataDelegate`. **No backend reads that return
value** (96 invocation sites, 0 consumers — [[TASK-451]]), so the skip has never happened: every `Update`
has always reached the store. TASK-316 removed the dead expression and left the write unconditional.

**Enabling the skip is a real behaviour change, not a tidy-up**, which is why it is its own decision:

- `AuditStoreWrapper`, `TimestampStoreWrapper` and `EventSourcingStoreWrapper` all sit **inside**
  `Store.Update` in `StoreWrapperBuilder`'s recommended chain, so a suppressed write drops the audit
  stamp, the `UpdatedAt` bump and the domain event.
- `VersionedStoreWrapper`'s optimistic check never runs for a skipped write.
- The cost it saves is one write per unchanged save; the cost it currently imposes is a SHA-256 over every
  model on **every read**, which buys nothing while the skip is inert.

## ⚠ Two traps already measured, so do not re-derive them

TASK-316 implemented the skip, measured these, and then backed it out. If this task re-enables it, both
are mandatory — each has a **live test already in the tree** that fails on the naive implementation:

1. **The baseline must be the row AS JUST READ, not the hash recorded at the caller's earlier read.**
   Otherwise a caller restoring a value someone else changed matches the stale hash and their write is
   silently dropped. Pinned by `An_update_that_restores_a_concurrently_changed_row_is_not_skipped`.
2. **The hash must be refreshed only AFTER the write succeeds.** Refreshing first records the new value as
   persisted even when the write throws, so the caller's retry sees "nothing changed" and loses the
   update. Pinned by `An_update_retried_after_a_failed_write_still_lands` (+ its async twin).

## Decision — 2026-11-04, by the user: always write; delete the hash and remove `ReadMode`

**Approved: no skip.** Every save keeps reaching the store, so the audit stamp, the `UpdatedAt` bump, the domain
event and the version check keep running. The hash apparatus is deleted: `_modelHash`, `StoreHash`, `CalculateHash`,
`CheckHashChange` and the hash-removal on delete, in the sync and async repositories and the bulk read path. Today it
serializes and hashes every model on every read, feeds nothing, and holds one entry per entity read for the
repository's lifetime.

**`ReadMode` is removed too, not kept as an inert property.** Its only job was to switch the hashing off. That is a
breaking change for **Affiliate** (`RepositoryProvider.InitReadMode`, `Startup.cs:61`, `AdminCategoryController`
around its admin writes), which is being moved into Symbio gradually. Affiliate's calls are deleted when it next builds
against the framework, and the change is recorded as breaking in the Recent Updates entry. Symbio does not use it.

This task is now the implementation: the "If not skipping" criteria below apply, and TASK-316's two
`…_STILL_issues_its_write` tests stay as the pins that every save writes (renamed to drop "STILL").

## Acceptance criteria

- [ ] A decision is taken and recorded: skip, or keep the write unconditional and **delete** the hash
      apparatus rather than leaving it computing a hash nothing reads
- [ ] If skipping: both traps above are implemented and their existing tests still pass, and the audit /
      timestamp / event-sourcing consequence is either accepted **in writing** or avoided by moving the
      decision below those decorators
- [ ] If not skipping: `CalculateHash` / `StoreHash` / `CheckHashChange` / `_modelHash` are removed or
      given a stated purpose, and `TASK-316`'s two `…_STILL_issues_its_write` pins are updated to say so
- [ ] `docs/specs/repository-contract.md` matches whichever answer is taken

## Human test plan

`N/A — fully covered by automated tests.`
