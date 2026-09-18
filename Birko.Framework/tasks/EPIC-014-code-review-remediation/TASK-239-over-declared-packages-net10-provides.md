---
id: TASK-239
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-234, TASK-229]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Packages declared that .NET 10 already provides — `NU1510`, the mirror image of TASK-234

## Context

Found while draining [[TASK-234]], and it is that task's **opposite**: TASK-234 is about a project using a
package it never declares; this is a project declaring a package it never needed.

```
error NU1510: PackageReference Microsoft.Extensions.DependencyInjection.Abstractions will not be pruned.
              This package is automatically available and does not need to be referenced explicitly.
```

`NU1510` is a **warning** by default and an **error** under the `-warnaserror` that `verify-conventions`
runs and that TASK-234's build sweep uses — so it is noise in every sweep, and it has already appeared in
three test projects across three different batches:

- `Birko.Data.Sync.RavenDB.Tests`
- `Birko.Security.AspNetCore.Tests`
- `Birko.Communication.WebSocket.Tests`

**Three is what was observed, not what exists.** They were noticed because a batch happened to build them;
nobody has swept for the code.

## Why it is worth more than a tidy-up

**`Birko.Packages.props` carries a `PackageVersion` for that exact package**, attributed to
`Birko.Data.Repositories`:

```
<PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.*" />
```

So the question is not only "which test projects over-declare" but **whether a shared project is declaring
something the framework already provides** — in which case TASK-234's ownership rule has been applied to a
package that should not be owned at all, and the entry in `Birko.Packages.props` is dead weight every CPM
consumer carries.

The same question applies to any other `Microsoft.Extensions.*` declaration in the family: several of those
became part of the shared framework in .NET 8–10, so a declaration written against an earlier target may now
be exactly this defect.

## Acceptance criteria

- [ ] A sweep reports every project emitting `NU1510`, by building with `-warnaserror` and matching the
      **code**, not a hand-listed set of project names — the three above are observations, not a census
- [ ] Each redundant `PackageReference` removed, and the project still builds and tests green
- [ ] **`Birko.Data.Repositories` answered specifically**: does it still need
      `Microsoft.Extensions.DependencyInjection.Abstractions` on net10, or is its declaration — and the
      `Birko.Packages.props` entry that exists to serve it — the same defect one layer up?
- [ ] Any `PackageVersion` left with no declaring projitems is removed from `Birko.Packages.props` in the
      same change, since that file's contract is "one entry per package a shared project declares"
- [ ] The framework-wide sweep is `-warnaserror` clean afterwards, so the next batch's failures are its own

## Out of scope

- TASK-234's under-declared packages. Same file, opposite direction, and the two should not be tangled: one
  is measured against `using` statements, this one against what the SDK ships.

## Human test plan

N/A — restore and build are mechanical.

## Implementation plan

_Populated by `/tasks plan TASK-239` — leave empty until then._
