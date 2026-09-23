---
id: TASK-475
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-09-19
depends-on: []
blocks: []
related: [TASK-210, TASK-230, TASK-473, TASK-474]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The first whole-tree vulnerability sweep since the monorepo migration

## Context

[[TASK-474]] repaired `audit-dependencies.ps1`, which had been reading **81 of 248** projects as a
whole-tree result since 2026-09-18 — a literal tab in `'Framework\tests'` silently dropped the entire
`tests/` bucket, and the `if (-not $buckets) { throw }` guard could not fire because `Consumers` still
resolved. That task deliberately did not run the repaired sweep, on the grounds that its output would
be **new findings** and belongs in its own task. This is it.

Run 2026-09-19. **248 projects audited, 0 unauditable.** That second number matters as much as the
first: a project whose restore fails produces no vulnerability rows, which is indistinguishable from a
clean one, and BardStudio's restore *was* failing until earlier the same day ([[TASK-473]]).

## Result: 6 findings across 6 projects, 3 distinct advisories, all High, all consumer-owned

| Advisory | Sev | Projects |
|---|---|---|
| `MessagePack` 2.5.192 | High | `DraCode.AppHost`, `Symbio.AppHost` |
| `Microsoft.OpenApi` 2.0.0 | High | `DraCode.KoboldLair.Server`, `DraCode.KoboldLair.Tests` |
| `Tmds.DBus.Protocol` 0.20.0 | High | `BardStudio.UI`, `Birko.Xaml.Gallery` |

**⚠ Zero framework-owned findings.** The 167 test projects that had been dark since the migration are
clean. That is a negative result and it is still the point of running this: nobody could have known
it, and [[TASK-230]] had found real advisories in exactly that half of the tree
(`Birko.Serialization.Tests`, `Birko.Messaging.Razor.Tests`, both `OpenTelemetry` rows). **The blind
spot was not hiding anything — which is only knowable by looking.**

Down from TASK-230's closing tally of *11 findings across 9 projects*. The difference is the
`SQLitePCLRaw.lib.e_sqlite3` family, cleared by [[TASK-473]]'s consumer work: one version declared
below the framework's floor was producing many advisory rows.

## ⚠ `Tmds.DBus.Protocol` — TASK-230's "none available" has expired

TASK-230 recorded this one as **accepted exposure** with *"none available"* as the reason. That was a
claim about another product's catalogue, and **§ Conventions rule 30 says such a claim has an expiry
date and nothing in the type system says so.** Re-measured today:

```
> Tmds.DBus.Protocol      (transitive)  resolved 0.20.0    latest 0.95.1
  Birko.Xaml.Gallery -> Avalonia.Desktop 11.2.3 -> Avalonia.X11 -> Avalonia.FreeDesktop -> Tmds.DBus.Protocol 0.20.0
```

A fixed version exists. The reason for accepting the exposure no longer holds, so the acceptance has
to be re-decided rather than inherited.

### ✅ RESOLVED 2026-09-19 — a MINOR bump was enough

Measured on throwaway probe projects rather than read off version numbers, which is the whole point
of the rule this was about:

| Avalonia | Tmds.DBus.Protocol | `--vulnerable` |
|---|---|---|
| 11.2.3 (was) | 0.20.0 | **High** |
| **11.3.22** (`11.*`) | 0.21.3 | **clean** |
| 12.1.2 | 0.94.1 | clean |

So the major was never needed. Avalonia floated to `11.*` in **6 places**: the framework's
`Birko.Xaml.Avalonia`, `Birko.Xaml.Shell` and `Birko.Xaml.Avalonia.Tests`, and the three consumers
that build them — `Birko.Xaml.Gallery`, `BardStudio` and `Latent`. All resolve 11.3.22 → 0.21.3, all
report **no vulnerable packages**, all build, and `Birko.Xaml.Avalonia.Tests` is **196/196**.

**⚠ Latent had already fixed it, and nobody noticed.** `Latent/Directory.Packages.props` carried a
transitive pin `Tmds.DBus.Protocol 0.21.3` with a comment naming this exact advisory. So the same
Avalonia 11.2.3 resolved **0.21.3** there and **0.20.0** in the other two — identical top-level
version, different resolved transitive, which is precisely the trap. **The framework's family-wide
record said "accepted, none available" while one of its own consumers had shipped the remedy.** A
finding recorded as accepted is not re-checked by anyone; that is what makes rule 30 expensive.

**⚠ The bump had to be all-or-nothing across four repos.** `Birko.Xaml.Avalonia` is a real `.csproj`
referenced by `ProjectReference`, not a `.projitems` — so it *does* create a package dependency edge.
Moving the framework to `11.*` while any consumer pinned `11.2.3` would have fired `NU1605` there,
the identical shape that broke `DraCode.KoboldLair` the same day. Latent needed changing even though
it was already clean, purely for that reason.

Avalonia **12** remains a separate, deliberate migration — a major, across three projects' AXAML,
with `LiveChartsCore.SkiaSharpView.Avalonia 2.0.5` alongside it. Both restore cleanly against 12
(probed), but restore is not compilation. Nothing security-related requires it now.

Two routes were considered, and they are not equivalent:

1. **Bump Avalonia.** `11.2.3 → 12.1.2` is available. A major, so it is a real piece of work in both
   `BardStudio.UI` and `Birko.Xaml.Gallery`, and it may or may not carry a fixed `Tmds.DBus.Protocol`
   — **check the resolved transitive, not the top-level number**, which is the trap TASK-230 recorded
   for SQLite (`Microsoft.Data.Sqlite` 10.0.0 was *newer and worse* than 9.0.19).
2. **Pin the transitive directly.** Cheaper, and the framework's own preference is to bump the
   top-level first and pin only when no bump clears it.

⚠ Also worth noting while here: `Birko.Xaml.Gallery` targets **net8.0** while the rest of the family
is on net10.0. Not this task's business, but it constrains which Avalonia is reachable.

## The other two

- **`MessagePack` 2.5.192** — Aspire transitive in two `*.AppHost` projects, both consumers. The
  framework declares `MessagePack 3.*` in `Birko.Serialization.MessagePack`, which is unaffected and
  unrelated; nothing here reaches the framework's declaration.
- **`Microsoft.OpenApi` 2.0.0** — a **direct** declaration in `DraCode.KoboldLair.Server` (`.Tests`
  inherits it), so it is the cheapest of the three to fix and the only one nobody has an excuse for.
  Already named as out of scope on DraCode's own TASK-076.

## Scope

All 6 are **consumer-owned**. [[TASK-230]]'s precedent is *report, not edit* — the framework's part is
to surface them. This task therefore ends at the report plus the three routes above; each consumer
owns its own fix, and the `Tmds.DBus.Protocol` re-decision is the one that needs a person rather than
a patch.

## Out of scope

- **Fixing any of the 6.** Consumer-owned, per above.
- **The dropped-bucket guard.** [[TASK-474]] left it open: the real defect there was that
  `Where-Object { Test-Path }` turns a typo into a smaller sweep with no diagnostic, and a guard for
  "none" is not a guard for "fewer than asked for". Still open, and this run does not close it — it
  only shows what the repaired scope reports today.

## Human test plan

N/A — the sweep is mechanical; the `Tmds.DBus.Protocol` decision is not, and is the one item here
that wants judgement.

## Implementation plan

_Populated by `/tasks plan TASK-475` — leave empty until then._

---

## Closed — 2026-09-23. Re-measured, and one claim above was wrong

**The deliverable of this task is the report, and § Scope says so explicitly** — *"the framework's
part is to surface them… this task therefore ends at the report plus the three routes above"*, with
`## Out of scope` naming *"Fixing any of the 6"*. That is done, and the `Tmds.DBus.Protocol` third
was resolved on 2026-09-19 by floating Avalonia to `11.*`. Closing it rather than leaving a finished
report open as `todo`.

### Re-measured 2026-09-23 — all four remaining findings are still live

Checked directly per project rather than by re-running the 17-minute sweep:

| Project | Advisory | Still present |
|---|---|---|
| `Symbio.AppHost` | `MessagePack` 2.5.192 High | yes |
| `DraCode.AppHost` | `MessagePack` 2.5.192 High | yes |
| `DraCode.KoboldLair.Server` | `Microsoft.OpenApi` 2.0.0 High | yes |
| `DraCode.KoboldLair.Tests` | `Microsoft.OpenApi` 2.0.0 High | yes |

### ⚠ "A **direct** declaration" was wrong, and it was the basis of a cost estimate

§ The other two calls `Microsoft.OpenApi` *"a **direct** declaration in `DraCode.KoboldLair.Server`…
the cheapest of the three to fix and the only one nobody has an excuse for."* **No csproj or props in
DraCode declares it.** `dotnet list --include-transitive` resolves it through
**`Microsoft.AspNetCore.OpenApi 10.0.0`**. So it is not a one-line version bump and the "no excuse"
framing does not hold — it is the same shape as the other two, a transitive nobody chose.

Corrected rather than quietly dropped, because the error was load-bearing: it ranked this finding as
the cheap one, which is how a remedy gets planned without being priced.

### The chains, measured

```
Aspire.Hosting.AppHost 13.2.4 -> Aspire.Hosting 13.2.4 -> StreamJsonRpc 2.22.23 -> MessagePack 2.5.192
Microsoft.AspNetCore.OpenApi 10.0.0 -> Microsoft.OpenApi 2.0.0
```

`Aspire 13.2.4` is already recent, so **a top-level bump may not clear MessagePack** — which has to
be probed, not assumed: the framework's own remediation note says *"check the RESOLVED transitive,
not the top-level number"*, and [[TASK-230]] recorded `Microsoft.Data.Sqlite 10.0.0` being newer and
**worse** than `9.0.19`. If no bump clears it, a transitive pin is the fallback, exactly as the
`Tmds.DBus.Protocol` third was solved in `Latent` before anyone noticed.

### What is still owed, and by whom

All four are **consumer-owned**, in `Consumers/DraCode` and `Consumers/Symbio` — separate repos with
their own task trees. Neither is reachable from this repo's CI: [[TASK-481]]'s nightly sweep checks
out **one** consumer (`Birko.Sandbox`) and therefore reports 0 findings while these 4 are live, which
is why that job is named *"framework + Sandbox only"*. **Nothing in this repo will notice if they go
unfixed**, which is the argument for filing them where they will be worked.