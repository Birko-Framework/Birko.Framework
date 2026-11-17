---
id: TASK-939
parent: null
feature: null
status: done
priority: P1
assignee: agent
created: 2026-11-17
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A fresh async XML store whose first operation is a write ignored the data on disk

## Context

Found while fixing [[TASK-931]]: `AsyncXmlBatchStore.CreateAsync` did not load from disk, only the first read did.
A probe across every XML and JSON store kind (a store seeded with 5 entities, then a NEW instance whose first call is
a write, then a third instance counting) measured the gap.

## Cause

`AsyncXmlSeparateStore.InitCoreAsync` created the directory but never loaded the data (`AsyncXmlStore`'s does). Its
write overrides, and `AsyncXmlBatchStore`'s, work on the in-memory items, so a fresh instance whose first call was a
write saw an empty store. Measured on main (Windows):

| First call on a fresh store | AsyncXmlBatchStore / BatchBulkStore | AsyncXmlSeparateStore / SeparateBulkStore |
|---|---|---|
| create | **1 of 6 entities left**: batch 0 rewritten with the new one, every other batch file deleted | correct |
| update of an existing entity | `EntityNotFoundException` | `EntityNotFoundException` |
| delete of an existing entity | silently nothing | silently nothing |

The other six XML kinds and all ten JSON kinds were correct in all three cases. Any process that opens an async XML
batch store and writes before reading (a background job appending a record, a fresh app instance) wiped the store.

## Change

`AsyncXmlSeparateStore.InitCoreAsync` ends with `EnsureDataLoadedAsync`, as `AsyncXmlStore` does. Every public
operation passes through Init first, and `AsyncXmlBatchStore` / the bulk variants inherit it.

## Evidence

- `Task939WriteBeforeReadTests` (XML): create / update / delete as the first call, on all ten XML store kinds. Red on
  main for the four async separate / batch kinds (10 of 30 cases), green with the change.
- The same tests on the JSON stores (a guard): 30 of 30 before and after.
- Birko.Data.XML.Tests 535/535, Birko.Data.JSON.Tests 450/450, Birko.Data.XML.ViewModel.Tests 7/7,
  Birko.BackgroundJobs.XML.Tests 17/17, Birko.Data.Sync.Xml.Tests 7/7, Birko.Workflow.XML.Tests 8/8.

## Acceptance criteria

- [x] A fresh async XML store keeps the existing data whatever its first write is.
- [x] Every XML and JSON store kind is covered, so a new override that forgets the load fails.

## Human test plan

N/A — fully covered by automated tests.
