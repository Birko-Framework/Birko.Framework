---
id: TASK-462
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-461]
findings: []
pr: null
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
