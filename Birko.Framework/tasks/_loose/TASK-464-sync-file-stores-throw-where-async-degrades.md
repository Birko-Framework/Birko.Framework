---
id: TASK-464
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

# An unconfigured sync file store throws where its async twin returns empty

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
