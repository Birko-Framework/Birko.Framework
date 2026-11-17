---
id: TASK-931
parent: null
feature: null
status: done
priority: P1
assignee: agent
created: 2026-11-15
depends-on: []
blocks: []
findings: [TASK-939]
pr: null
github-issue: null
jira-key: null
---

# XML batch stores lost an entity when a save failed partway (`A_failed_async_batch_save_loses_no_batch` flaky on CI)

## Context

build-and-test run 43700238553 (db86c349, 2026-11-15) failed on
`Task721XmlStoreSafetyTests.A_failed_async_batch_save_loses_no_batch`, 1 of 502 in Birko.Data.XML.Tests; its re-run on
the same commit passed. It failed again on run 44150948829 (257c41f4, 2026-11-17, a push of SQL and Web-docs changes
only) and passed on re-run. It passed every local run on Windows.

## Cause

The XML batch stores (`XmlBatchStore`, `AsyncXmlBatchStore`) load their batch files in the order
`Directory.GetFiles` returns, and the next save numbers the batches by that order. `GetFiles` promises no order:
Windows lists by name (`items-10.xml` before `items-2.xml`), Linux in the file system's own order. So a save could move
entities between files, and a save that failed partway left one entity overwritten and another stored twice. A
TASK-721 guarantee ("a failed batch save loses no batch") did not hold.

The flaky test reaches it like this. `CreateAsync` does not load from disk, but the first `ReadAsync` does (that gap is
[[TASK-939]]), so the test's `ReadAsync(b)` reloads `items-0` and `items-1` in listing order. When Linux lists
`items-1` first, the update writes `b2` into `items-0` and the injected failure hits `a`, which is what CI saw.

The earlier hypothesis (concurrent batch writes) was wrong: the store writes its batches one after another.

## Reproduction (rule 54)

- **Windows, deterministic:** twelve one-entity batches make `GetFiles` order differ from batch order. A fresh store
  loads them, an update fails on its 4th write, and a reader finds **11 of 12** entities (sync and async alike).
- **Linux:** the original test passed 60 of 60 runs with the worktree mounted from Windows (the files sat on NTFS,
  which lists sorted) and 40 of 40 with the source copied onto the container's file system. On one file system the
  two names list in the same order every run, so a single machine is expected to give all-pass or all-fail. The
  Linux failure is explained by the code path but was not reproduced locally; it is recorded, not claimed.

## Change

- `RecordFiles.ListByIndex(directory, template)` (Birko.Helpers): the batch files in batch order (by number, so
  `items-2` before `items-10`; a key longer than `long` still sorts).
- `XmlBatchStore.LoadData` and `AsyncXmlBatchStore.LoadDataAsync` load through it, so a save writes every entity back
  into the file it came from.
- JSON batch stores name their files by Guid, not by index, and are not affected.

## Evidence

- New tests in `Task721XmlStoreSafetyTests`: a failed save of a store loaded from twelve batches loses none (sync and
  async), and a save keeps every entity in its batch file. Red on main (11 of 12, and `items-2.xml` no longer held `e2`), green
  with the change.
- `RecordFilesTests.Batch_files_are_listed_in_batch_order_not_name_order`.
- Birko.Data.XML.Tests 505/505, Birko.Data.XML.ViewModel.Tests 7/7, Birko.Data.JSON.Tests 420/420,
  Birko.Helpers.Tests 127/127.

## Found

- [[TASK-939]] (P1): a fresh `AsyncXmlBatchStore` / `AsyncXmlBatchBulkStore` whose first operation is a write does not
  load from disk first, so it overwrites batch 0 and deletes every other batch file (measured: 1 of 6 entities left).

## Acceptance criteria

- [x] Reproduced (deterministically on Windows; Linux recorded as not reproducing on one file system, rule 54), with
  the assertion that failed.
- [x] Root cause in the store, not the test: a product defect against TASK-721's guarantee, fixed in the store.
- [x] The new tests fail for the right reason only: batch order is forced, not left to the platform.

## Human test plan

N/A — fully covered by automated tests.
