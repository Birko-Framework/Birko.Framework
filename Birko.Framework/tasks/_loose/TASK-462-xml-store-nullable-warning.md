---
id: TASK-462
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P3
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-461]
findings: []
pr: 865f60be
github-issue: null
jira-key: null
---

# Two warnings a consumer sees on every build

## 1. CS8602 in `AsyncXmlSeparateStore`

Found by [[TASK-461]]: the Sandbox began importing `Birko.Data.XML`, and the build surfaced

```
Birko.Data.XML/Stores/AsyncXmlSeparateStore.cs(153,31): warning CS8602:
    Dereference of a possibly null reference.
```

Line 153 is `var fileName = $"{_settings.Name}-{data.Guid.Value}.xml";` — `_settings` is nullable and
is dereferenced without a check. § Code Style says *"All new code must compile without CS8600–CS8605,
CS8618, CS8625"*, so this is a standing violation rather than a new one; it was simply invisible
while nothing outside the XML project's own tests compiled that file.

It is a **warning**, not a defect: `SetSettings` is what populates `_settings`, and every path that
reaches `CreateCore` has gone through `EnsureInitialized`, which cannot run without settings. So the
dereference is almost certainly unreachable in practice. That is a reason to fix it cheaply and
correctly, not a reason to suppress it.

### Acceptance

1. The warning is gone, and the fix states *why* the value cannot be null at that point rather than
   silencing it with `!` — if `!` is genuinely the right answer, the comment says what guarantees it.
2. Check the **sibling stores in the same project** before closing: `AsyncXmlSeparateStore` has a
   batch twin and a sync counterpart, and a rule fixed in one of four places is the shape
   § Conventions keeps recording. Grep for the same `_settings.` dereference pattern across
   `Birko.Data.XML` and `Birko.Data.JSON` (which has the same store layout) and report the count,
   even if the answer is one.
## 2. NU1510 from `Birko.Data.Repositories`

The same build prints, on every run:

```
warning NU1510: PackageReference Microsoft.Extensions.DependencyInjection.Abstractions will not be
    pruned. This package is automatically available and does not need to be referenced explicitly.
```

Source: `Birko.Data.Repositories.projitems:18-19`, which declares the package twice (once for central
package management, once without). On `net10.0` that assembly ships with the framework reference, so
the declaration is redundant and NuGet says so — and **every consumer importing Repositories sees
it**, which is most of them.

Do **not** just delete the lines: check whether any supported TFM still needs the explicit reference.
If one does, condition the reference on the TFM rather than removing it; if none does, remove both
lines. Either way say which, because a redundant declaration and a load-bearing one look identical.

### Acceptance

1. Either both lines are removed, or the reference is conditioned on the TFM that needs it — and the
   task says which, with the measurement behind it.

## Both

`dotnet build` of `Birko.Sandbox` reports **0 warnings**. It is the cheapest place to check, because
it imports 38 projects through `$(BirkoSrc)` exactly as a real consumer does.

## Why P3

Neither is a defect. But a build that always prints two warnings is a build whose warnings nobody
reads, which is the same failure § Conventions records for a diagnostic channel with no subscriber —
and the Sandbox's build output is now a CI step, so the noise is in front of everyone.


---

## Outcome (2026-09-18)

**The whole framework now builds with 0 warnings.** Four distinct ones existed, not two, and **three
of the four were false positives** — two of them with remedies that would have done real damage.

### 1. CS8602 in `AsyncXmlSeparateStore` — real, and wider than the warning

The value genuinely **can** be null, so `!` was never the right answer. `SetSettings` is a plain
assignment nothing enforces, and `InitCore` silently no-ops without settings (its `is Settings`
pattern just fails) — so lazy init reported success and the first create dereferenced null.
Measured: `NullReferenceException`, thrown **after** the entity had been added to the in-memory
dictionary, leaving the store holding a row no file backed.

Fixed with `RequireSettings()` on both XML store bases, called **before** the dictionary is touched.

**⚠ The compiler pointed at half of it.** The async family declares `protected Settings? _settings`
and warned; the sync family declares `Settings _settings = null!` and said nothing about
line-for-line identical code. *The sync store was never safer, only quieter* — so the guard went on
both twins. Mutation B (revert the sync half alone) reds **3 of 24**, which is the whole argument.

Criterion 2's sweep: JSON is **not** affected — its separate stores guard every site with
`_settings?.Name` and defer file writing to a guarded flush. XML's create path was the only
unguarded dereference in either family.

### 2. NU1510 — measured, and the advice is wrong on both counts

- **`Microsoft.Extensions.DependencyInjection.Abstractions`** (Birko.Data.Repositories): removing it
  **fails the build on net8.0 AND net10.0** (CS0234/CS0246 in `ServiceCollectionExtensions.cs`),
  measured with a probe console app. The assembly reaches a project through the ASP.NET Core
  framework reference, which a console or class-library consumer does not have. NuGet's "automatically
  available" is true for web projects only.
- **`Microsoft.Extensions.Caching.Memory`** (Birko.Messaging.Razor): a deliberate **CVE override** —
  it exists to beat RazorLight 2.3.1's hard-pinned vulnerable 6.0.0 transitive
  (GHSA-qj66-m88j-hmgj, high). Removing it reintroduces a high-severity advisory.

So this task's own acceptance ("either remove both lines, or condition on the TFM") is answered
**neither** — the premise was wrong. `NoWarn` at each `.projitems`, with the measurement written
beside the reference. **Cost stated rather than hidden:** a consumer importing either projitems stops
seeing NU1510 for its own packages too; accepted because NU1510 only ever says "you may delete this".

### 3 + 4. Two more the task did not know about

- **CS0414 in `BluetoothLE`** — the field's *readers* live inside `#if WINDOWS` / `#if LINUX` while
  its writers are unconditional, so a default build assigns and never reads it. Correct warning,
  inert field; suppressed with the reason rather than conditioning three unrelated writes.
- **CA2255 in `Birko.Data.SQL.View`** — a module initializer in a library. Under the shared-project
  model it *is* application code (`.projitems` compile into the consuming assembly), which is exactly
  the property the existing remarks rely on. The analyzer cannot see that distinction.

### Mutations (three disjoint, all red)

| Mutation | Result |
|---|---|
| A — revert the async guard only | 2 of 24 |
| B — revert the **sync** guard only | 3 of 24 |
| C — move the guard *after* `_items.Add` | 2 of 24 (exactly the "leaves no row behind" pair) |

C is why the guard's **placement** is load-bearing rather than cosmetic: it still throws the right
exception and still fails.

### Also fixed, and MongoDB is the interesting one

The two MongoDB CS8602s are a flow-analysis false positive — **every** caller of `FindIn`/`CountIn`
already guards `Collection == null`. Rather than `!` plus a comment, the helpers now **take** the
collection, so the compiler enforces the invariant the callers keep and no test is needed to pin it.
⚠ And note why only one of two identical hazards was visible: `Find` is an **extension** method, so a
null receiver never warns; `CountDocumentsAsync` is an instance method.

## Out of scope

- **The sync file stores NRE where the async ones degrade.** Every `_settings` dereference in the
  *sync* XML stores (`XmlBatchStore:74`, `XmlSeparateStore:56,93`, …) is bare, including inside the
  very guards meant to protect them, while the async twins use `_settings?.`. So an unconfigured sync
  store throws where the async one returns empty. Invisible because `null!` suppresses it. Not a
  warning and not what this task is about. → [[TASK-464]]
- **`SetSettings(ISettings)` silently does nothing** when the argument is not a `Settings` — the
  SH-H037 silent-drop family, in both XML and JSON store families. → [[TASK-464]]
