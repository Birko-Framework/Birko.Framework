# ADR-002 — The root helper scripts are .NET 10 file-based apps, not PowerShell

- **Status:** Accepted, 2026-09-19
- **Supersedes:** nothing
- **Superseded by:** nothing
- **Task:** TASK-476
- **Applies to:** `audit-declarations.cs`, `audit-dependencies.cs`, `audit-consumer-versions.cs`,
  `install-skills.cs`, `tools/AuditCommon/`

## Context

The aggregator root shipped four committed tooling scripts as PowerShell. PowerShell 7 is itself
cross-platform, so the language was never the blocker — **every one of them was written with Windows
path assumptions**, and three failed on Linux by silently answering the wrong question rather than by
erroring:

- `audit-consumer-versions.ps1` reported **every consumer as importing nothing**, because consumer
  imports are `$(BirkoSrc)\Birko.Helpers\Birko.Helpers.projitems` — the MSBuild file format, which is
  backslash-separated on every platform, across all 173 projitems in the tree. Its own header names
  that state as *"a defect in this script, not a clean result."*
- `audit-dependencies.ps1` dropped the `Framework\tests` bucket and swept only `Consumers` — the same
  expression, and the same defeated `if (-not $buckets) { throw }` guard, as [[TASK-474]]'s literal tab.
- `audit-declarations.ps1` admitted generated `obj/**/*.cs` into its `using` scan.
- `install-skills.ps1` used `New-Item -ItemType Junction`, which is Windows-only.

The constraint that selected the remedy, from the user: **pwsh is present on some of the family's
machines and unknown on the rest, and a second implementation is not something they will maintain.**
That pair rules out both a pwsh-portability fix and bash siblings.

## Decision

Rewrite all four as **.NET 10 file-based apps** — a single `.cs` file run with `dotnet run foo.cs`,
no `.csproj`, no tool install. Delete the `.ps1` originals. Shared path handling gets **one producer**
in `tools/AuditCommon/`, referenced via `#:project`.

## Why this shape and not the alternatives

| Shape | Needs on a Linux box | First run | After an edit | Unchanged |
|---|---|---|---|---|
| `.ps1` | **pwsh** — cannot be guaranteed | 4.2s | 4.2s | 4.2s |
| `.csx` (dotnet-script) | **`dotnet tool install -g dotnet-script`** | ~2s | 2.0s | 0.9s |
| **`.cs` file-based app** | **nothing — SDK only, already required** | ~13s¹ | 1.0s | 0.6s |
| bash siblings | nothing | — | — | — |

¹ first-ever run on a machine seeds the build directory. All measured 2026-09-19 on the 173-projitems
tree, BCL only, no `#:package` directives.

- **Bash siblings were refused on the maintenance cost**, which is the constraint itself. Two
  implementations of one audit is the twin-drift shape § Conventions records at TASK-245: *the twin you
  patched may not be the one anything calls.*
- **`.csx` was rejected on its dependency, not on speed** — it has the faster cold start of the two C#
  options. It needs `dotnet tool install -g dotnet-script` on machines nobody can survey, which is the
  same class of problem as requiring pwsh. Worth recording that the `csharp-script` skill's *"~15–20s
  cold start"* figure **did not hold here**: it assumes a `#r "nuget:"` restore, and these are BCL-only.
- **A console project (`.csproj` + `.slnx` registration) was rejected as heavier for no gain.** The
  New Project Checklist in `CLAUDE-maintenance.md` exists for shipped shared projects; these are
  tooling. A file-based app is the same C# without the registration surface.
- **The `csharp-script` skill's *"never in the repo tree"* rule does not forbid this, and reading it
  carefully is what selected the shape.** That rule governs loose `.csx` scratchpad files; it says the
  alternative for something worth keeping is *"a real project with a recorded decision."* This document
  is that decision.

## Consequences

- **The scripts are 4–7× faster than what they replaced**, which was not the goal and is not the reason.
- **`dotnet run` is the invocation.** Every reference in `CLAUDE.md` and `CLAUDE-maintenance.md` was
  updated. **Closed task files were deliberately left alone** — TASK-210/229/230/234/267/473/474/475
  narrate these scripts under their `.ps1` names, and that is the record of what they were called at
  the time, not a stale pointer to fix.
- **`--fail-on-finding` and `-FailOnFinding` are both accepted.** Eight closed write-ups quote the
  PowerShell spelling and a reader who types it should not get a shrug.
- **Exit 2 is reserved for a bad argument**, distinct from the 1 that `--fail-on-finding` returns: a
  scheduled job that cannot tell *"the audit found something"* from *"I mistyped the flag"* will
  eventually read the second as the first.
- **`tools/AuditCommon/` is deliberately not named `Birko.*` and is not in `Birko.Framework.slnx`.**
  `audit-declarations` enumerates `Birko.*` directories at the repo root, so a helper called
  `Birko.Audit.Common` would be swept by the audit it exists to serve.
- **⚠ `install-skills` does not use the portable link API on Windows.** A directory symlink there needs
  Developer Mode or elevation; a junction needs neither. `Directory.CreateSymbolicLink` would have been
  a portability fix that broke the platform the script already worked on, so Windows shells out to
  `mklink /J` and only Linux takes the symlink.
- **A rewritten checker is a new checker.** Each audit was proved byte-identical against the output of
  the script it replaces, captured before the originals were deleted, **and** each inherited defect was
  re-proven against its own fixture. Byte-identical output alone would not have been sufficient.
- **Untested from Windows:** the `#!/usr/bin/env dotnet` shebang, which should make these directly
  executable (`./audit-dependencies.cs`) on Linux. Added; needs confirming on a Linux box.

## What would reverse this

Shipping the Birko backends as real NuGet packages deletes `audit-consumer-versions` entirely (NuGet's
own `NU1605` would see the downgrade across a package edge), and would shrink the rest. That is
deferred until the libraries stabilise — see `CLAUDE-maintenance.md` § External dependencies.
