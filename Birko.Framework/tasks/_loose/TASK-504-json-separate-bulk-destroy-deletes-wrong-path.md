---
id: TASK-504
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
related: [TASK-499]
findings: [FIELD-018]
pr: null
github-issue: null
jira-key: null
---

# Destroying a populated JsonSeparateBulkStore / JsonBatchBulkStore throws

## Context

Found on 2026-09-26 from the field, by the Affiliate consumer (`BirkoWorks/Affiliate`), once TASK-499 let its
import run end to end. At the end of each run the importer destroys and re-creates its compare records, a
`JsonBatchBulkStore<CompareResult>` with `Location = Compare/<site>` and `Name = *.json`. On every run after the
first it crashed:

```
System.IO.DirectoryNotFoundException: Could not find a part of the path '/app/Compare/yetunabytek.cz/*.json'.
   at System.IO.Directory.Delete(String path)
   at Birko.Data.JSON.Stores.JsonSeparateBulkStore`1.Destroy() in …/JsonSeparateBulkStore.cs:line 69
```

`JsonSeparateBulkStore.Destroy()` deletes the matching files and then calls `Directory.Delete(Path!)`. `Path`
is the store directory **joined with its file `Name`**, not the directory. With a wildcard Name that path is
invalid (`IOException` on Windows, `DirectoryNotFoundException` on Linux). With a plain Name it is a file
that was just deleted, so `DirectoryNotFoundException` either way. `JsonBatchBulkStore` inherits the method.
The async twin `AsyncJsonSeparateBulkStore` deletes `pathDirectory` and is correct.

`JsonSeparateStore.Destroy()` also calls `Directory.Delete(Path)`, but it is **not** affected: for that store
`Location/Name` is its own directory (it enumerates `Directory.GetFiles(Path, …)`), so `Path` is right.
The existing CR-H050 test covers it.

## Acceptance criteria

- [x] `Destroy()` on a populated `JsonSeparateBulkStore` and `JsonBatchBulkStore` does not throw, removes the
      store directory, and a fresh store on the same settings can create and read again. Covered for both a
      plain (`data.json`) and a wildcard (`*.json`) Name: `SeparateStoreDestroyTests`, 4 cases.
- [x] All 4 cases fail without the fix and pass with it. The full `Birko.Data.JSON.Tests` suite passes (37).

## Out of scope

- `JsonSeparateStore` requiring its base directory to exist before `SetSettings`, while the bulk stores
  create it. That is a difference between the stores, not this defect.

## Human test plan

N/A: covered by the automated tests. The consumer-side check (two consecutive Affiliate import runs) is part
of Affiliate TASK-003's human test plan.

## Progress log

- 2026-09-26 — Red: 4/4 `SeparateStoreDestroyTests` cases on `2b8663c1`. Fix: `Directory.Delete(PathDirectory!)` in `JsonSeparateBulkStore.Destroy`. Green 4/4; JSON suite 37/37.
