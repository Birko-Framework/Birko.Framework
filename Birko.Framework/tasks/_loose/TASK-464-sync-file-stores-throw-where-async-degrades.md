---
id: TASK-464
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P3
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-462]
findings: []
pr: null
github-issue: null
jira-key: null
---

# ~~An unconfigured sync file store throws where its async twin returns empty~~

**Retitled on measurement: an unconfigured file store ACCEPTED writes and persisted nothing.**

Spawned by [[TASK-462]], which fixed the one site the compiler could see and measured two more it
could not.

## 1. The twins disagree, and `null!` is what hides it

The two file-store families declare the same field differently:

| | Declaration | Effect |
|---|---|---|
| `AsyncXmlStore` / `AsyncJsonStore` | `protected Settings? _settings = null;` | dereferences warn, so every site was written `_settings?.` and **returns empty** when unconfigured |
| `XmlStore` / `JsonStore` | `protected Settings _settings = null!;` | nothing warns, so every site is bare and **throws `NullReferenceException`** |

Measured sites with a bare dereference on the sync side: `XmlBatchStore:74,84,94`,
`XmlSeparateStore:56,93`, `JsonBatchStore`, `JsonSeparateStore` — including *inside the very guards*
meant to protect them (`if (… || string.IsNullOrEmpty(_settings.Name))` dereferences before it can
decide anything).

So the same operation on the same data has two behaviours depending on which twin a consumer picked,
and the divergence is invisible because `null!` asserts a guarantee nothing provides. TASK-462 fixed
only the create path, where the failure was a *lost write*.

### What this needs

Decide one answer and apply it to both families, rather than converging from symmetry:

- If **degrade** is right, the sync stores adopt `_settings?.` and the declaration drops `null!`.
- If **refuse** is right, the async stores stop returning empty and call `RequireSettings()` —
  which TASK-462 already added to all four bases.

⚠ Whichever way: the *read* asymmetry TASK-462 deliberately kept is a separate question and is
pinned by a test there (a write refuses, a read answers empty). Do not delete that pin without
arguing with it — § Conventions records why a write must never report success.

## 2. `SetSettings(ISettings)` silently does nothing on a type mismatch

```csharp
public virtual void SetSettings(ISettings settings)
{
    if (settings is Settings settings1)
    {
        SetSettings(settings1);
    }
}
```

No `else`. A caller passing an `ISettings` that is not a `Settings` gets **no exception, no log, and
an unconfigured store** — the SH-H037 silent-drop family exactly. It is present in both the XML and
JSON store families, sync and async.

This is also *how a consumer reaches the state TASK-462 guards against without doing anything
obviously wrong*: they called `SetSettings`, and it did nothing.

### Acceptance

1. One stated behaviour for an unconfigured store per operation class, applied to all four bases, and
   a test per twin — the fix is only believable if the mutation reds the sync side, which is the half
   no warning covers.
2. `SetSettings(ISettings)` refuses a type it cannot honour, naming the expected type. Measure the
   blast radius first (§ SH-H037 requires it): grep consumer repos for `SetSettings(` with an
   `ISettings`-typed argument before turning silence into a throw.
3. The `null!` declarations are removed or justified in place. A guarantee nothing enforces is a
   comment, and this one cost a real defect.


---

## Outcome (2026-09-19) — the premise was wrong, and the truth was worse

The task predicted *"the sync stores throw `NullReferenceException` where the async ones degrade"*.
Measured on the shipped code with a probe consumer before changing anything:

| Store (unconfigured) | Before | After |
|---|---|---|
| `JsonStore` / `JsonSeparateStore` / `JsonBatchStore` `.Create` | **no exception** | `InvalidOperationException` |
| `XmlStore` / `XmlBatchStore` `.Create` | **no exception** | `InvalidOperationException` |
| `AsyncJson*` / `AsyncXml*` `.CreateAsync` | **no exception**, returned a real Guid, `Count == 1` | `InvalidOperationException` |
| any `.Read()` | empty | empty *(unchanged, deliberately)* |
| `SetSettings(ISettings)` with a foreign type | **silently ignored** | `ArgumentException` naming the type |

**Nothing threw.** Every write was accepted, reported success, and reached no disk — because each
persistence method opened with a guard that `return`ed when there was no path or name. So the defect
was not a loud sync/async divergence; it was **silent data loss on every file store, both twins,
both formats**. § Conventions' own rule, violated across eight classes: *a write that cannot be
applied must never report success.*

The predicted NRE never happens, because the bare dereferences the task listed sit *inside* those
early-return guards, which return before reaching them.

### The contract, stated once

**A write refuses; a read degrades.** `EnsureWritable()` is called as the first statement of every
write seam — 48 call sites across the four abstract bases and the four XML stores that override the
seams — and is overridden in exactly the four classes that own `_settings`, which is what lets the
separate- and batch-file stores inherit it. The read half is unchanged and asserted, because a read
answering empty for a store with no data is truthful, and refusing there would turn a working
degrade into a start-up failure for anything that probes before configuring.

**First statement, not last.** Mutation B moves the call after the in-memory dictionary is updated:
still the right exception, still 3 red — a caller who catches the refusal would otherwise be left
holding a row no file backs.

### `SetSettings(ISettings)` was free to fix, and that was measured

`Settings` is the **only** implementation of `ISettings` across the framework and all consumer repos
— every other match is a `where TSettings : ISettings` constraint. So the refusal cannot fire for any
type that ships today, and the test had to invent the first one. § SH-H037 requires the blast radius
before turning silence into a throw; here it is zero, and the silence was how a caller ended up
believing both that they had configured the store *and* that their writes were saved.

### Mutations (five disjoint, all red where they should be)

| Mutation | JSON | XML |
|---|---|---|
| A — the hook's override becomes a no-op | 7 | 7 |
| B — guard moved after the in-memory mutation | 0 | 3 |
| C — JSON family only loses the check | 6 | 0 |
| D — one seam silently drops the call | 0 | 2 |
| E — `SetSettings` goes back to ignoring a foreign type | 1 | 1 |

C proves the two families are covered independently; **D is the one that matters for the future** —
it is caught by `WriteSeamGuardCoverageTests`, which scans both projects and fails when a write seam
does not call the guard first. The behavioural tests prove today's stores are right; that scan is
what makes the *next* store right, since a missing call has no failing assertion of its own.

### Verified

10 suites green (JSON 33, XML 32, BackgroundJobs.JSON 7, BackgroundJobs.XML 7, Workflow.JSON 16,
Workflow.XML 8, Sync.Json 7, Sync.Xml 7, Data.Core 102, Composition 21), the whole framework builds
with **0 warnings**, and the Sandbox consumer harness is 26/26.

## Out of scope

- **The `null!` declarations are still there.** `XmlStore`/`JsonStore` declare
  `protected Settings _settings = null!` while their async twins declare `Settings?`. Criterion 3
  asked for these to be removed or justified; they are now **justified** — every write goes through
  `EnsureWritable()`, so the remaining bare dereferences are provably reached only after the check.
  Flipping the declaration would produce warnings on paths that are now correct, which is churn
  rather than safety. Recorded rather than done.
