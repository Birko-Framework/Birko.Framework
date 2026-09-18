---
id: TASK-235
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: blocked
priority: P3
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-229, TASK-230]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `FisData.Stock.Angular.Server` will fail `NETSDK1087` when its net10 migration lands

> **Blocked 2026-08-17 on an external condition, not on another task.** The project already cannot build for
> an unrelated, pre-existing reason (below), and its repository holds substantial uncommitted migration work
> that is not ours to finish. This becomes actionable the moment that migration builds. Held out of the
> ready pool deliberately so `fix-next` does not pick work that cannot be verified.

## Context

Filed as a **courtesy warning to a consumer**, not as framework work. Recorded here because it is a
consequence of a framework convention change and would otherwise be discovered by whoever next tries to
build that project — with no way to connect it to its cause.

[[TASK-229]] gave `Birko.Data.Tenant` a `FrameworkReference` for `Microsoft.AspNetCore.App`, because its
`Middleware/` uses `Microsoft.AspNetCore.Http`, `.Builder` and `.Routing` and had been getting them
transitively from `RavenDB.Client`. **A duplicate `FrameworkReference` is a hard error** — `NETSDK1087` —
unlike a duplicate `PackageReference`, which is only `NU1504`.

`FisData.Stock.Angular.Server` imports `Birko.Data.Tenant.projitems` **and** declares its own
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`. So it will fail to build once it can build at
all. The fix is deleting that one line.

**Why it is not simply fixed now.** Two reasons, both worth stating:

1. **The project is already unbuildable for an unrelated reason.** Its imports resolve to
   `C:\Source\Birko\Consumers\Birko.Data.Core\…` — pre-`$(BirkoSrc)` relative paths that point inside
   `Consumers/` rather than at the framework checkout. `MSB4019`. That is mid-migration state, not
   something this change caused.
2. **That repository holds substantial uncommitted work** — a net9 → net10 migration with the whole import
   list rewritten to `$(MSBuildThisFileDirectory)`. Editing around it risks losing someone's in-flight work.

**A correction that belongs in the record.** While cleaning duplicate `FrameworkReference` items,
`Consumers/*` was swept **by glob rather than by ownership**, and this file was edited. The line was
restored, so the repository carries nothing of ours. One detail could not be recovered: its
`FrameworkReference` came from the *uncommitted* work rather than from `HEAD`, so git could not supply its
original position — it is now first in its `ItemGroup`. Cosmetic, but real, and better said than not.
`Birko.Sandbox` and `Birko.Xaml.Gallery` are Birko-owned and were legitimately in scope; this one was not.

## Acceptance criteria

- [ ] Once the net10 migration builds, remove the duplicate
      `<FrameworkReference Include="Microsoft.AspNetCore.App" />` from
      `FisData.Stock.Angular.Server.csproj` — it now comes from `Birko.Data.Tenant.projitems`
- [ ] Verify by building, not by inspection: `NETSDK1087` gone
- [ ] Check the other FisData projects the same way — `FisData.Stock.API`, `.Core`, `.Web` — since any that
      import `Birko.Data.Tenant` and declare their own `FrameworkReference` have the identical problem, and
      all three carry large uncommitted change sets that were deliberately not inspected
- [ ] Confirm the `FrameworkReference`'s position in the `ItemGroup` reads as intended, given it was
      re-inserted by us rather than restored from git

## Out of scope

- The `MSB4019` import-path failure and the net10 migration itself. Consumer work, in flight, and not ours.
- Every other consumer. Measured: only this one and the Birko-owned `Birko.Sandbox` both imported
  `Birko.Data.Tenant` and declared a `FrameworkReference`; the sandbox is fixed and committed.

## Human test plan

- [ ] Build `FisData.Stock.Angular.Server` after the migration lands and confirm it succeeds. This cannot be
      checked now — the project does not build for reasons unrelated to the change this task is about.

## Implementation plan

_Populated by `/tasks plan TASK-235` — leave empty until then._
