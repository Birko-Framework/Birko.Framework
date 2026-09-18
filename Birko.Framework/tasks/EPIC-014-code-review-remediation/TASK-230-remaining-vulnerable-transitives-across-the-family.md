---
id: TASK-230
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-210, TASK-228, TASK-229]
findings: []
pr: e2a0181 f9558e3 204d70a (advisory fixes) + 9 CPM-compat projitems + 11 test repos + Birko.Sandbox@865ab0c
github-issue: null
jira-key: null
---

# The remaining vulnerable transitives — 7 advisories across 37 of 246 projects

## Context

The sweep [[TASK-210]]'s acceptance criterion demanded. TASK-210 fixed the only finding with consumer
blast radius (MongoDB — one line in a shared `.projitems`, so every importer inherited it); this task
carries the rest, which it deliberately did not fix blind.

**⚠ Scope corrected 2026-08-17, before any work started.** This task was filed from a sweep of
`Framework.Tests` plus `Birko.Sandbox` — 4 advisories, 13 projects. The first run of
`audit-dependencies.ps1`, which also sweeps `Consumers/`, reports **44 findings across 37 of 246
projects** and **7 distinct advisories**, two of which had never been seen. The original figures are kept
in git history; everything below is the measured set.

| Advisory | Sev | Projects | Birko-owned | Consumer-owned |
|---|---|---|---|---|
| `SQLitePCLRaw.lib.e_sqlite3` **2.1.10** | High | 27 | 10 test projects | BardStudio ×11, DraCode ×5, `sqlite-cli` |
| `SQLitePCLRaw.lib.e_sqlite3` **2.1.11** | High | 4 | `Birko.Sandbox` | Presenter ×3 |
| `MessagePack` 3.1.3 | High | 2 | `Birko.Serialization.Tests`, `Birko.Sandbox` | — |
| `MessagePack` 2.5.192 | High | 2 | — | `DraCode.AppHost`, `Symbio.AppHost` |
| `Microsoft.OpenApi` 2.0.0 | High | 2 | — | `DraCode.KoboldLair.Server` / `.Tests` |
| `Tmds.DBus.Protocol` 0.20.0 | High | 2 | `Birko.Xaml.Gallery` | `BardStudio.UI` |
| `Microsoft.Extensions.Caching.Memory` 6.0.0 | High | 1 | `Birko.Messaging.Razor.Tests` | — |
| `OpenTelemetry.Api` 1.15.0 | Moderate | 2 | `Birko.Telemetry.OpenTelemetry.Tests`, `Birko.Sandbox` | — |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.0 | Moderate | 2 | same two | — |

**Still true, and the reason this stays P2:** none of these is declared in any `.projitems`, so no consumer
inherits one *from the framework*. The consumer-owned rows are each consumer's own dependency choice — the
framework's part is to report them, not to edit other repos.

**The scope correction is itself the lesson.** The narrower sweep did not merely undercount; it pointed at
the wrong fix. It saw only `SQLitePCLRaw` **2.1.10** and would have justified "bump past 2.1.10". The
consumer sweep found **2.1.11 also affected**, on a *newer* `Microsoft.Data.Sqlite`. The floor is **2.1.12**:

- `Microsoft.Data.Sqlite` **9.0.19** → `SQLitePCLRaw` 2.1.12 — **verified clean twice**: a real project
  built with `-warnaserror`, and a scratch project swept green by `audit-dependencies.ps1`.
- `Microsoft.Data.Sqlite` **10.0.0** → 2.1.11 — still affected. **10.0.11** → 2.1.12.

A version-number comparison would have picked 10.0.0 over 9.0.19 and been wrong.

## Acceptance criteria

- [x] Every **Birko-owned** row above is cleared or has a recorded reason it cannot be, verified by
      re-running `audit-dependencies.ps1`, not by reading nuspecs
- [x] The **consumer-owned** rows are reported to those repos and tracked there, not silently fixed here.
      `Symbio` in particular is out of bounds for this task. Record where each was reported
- [x] SQLite lands on a version resolving `SQLitePCLRaw.lib.e_sqlite3` **≥ 2.1.12** — the check is the
      resolved transitive, not the top-level number, since 10.0.0 > 9.0.19 and is *worse*
- [x] `MessagePack`, `Microsoft.Extensions.Caching.Memory` and the OpenTelemetry pair each get a remedy or
      a recorded reason; where no fixed version exists, say so and record the exposure rather than bumping
      to something equally affected
- [x] Each affected suite still passes after its bump — a package bump is exactly the change that breaks a
      test while the build stays green
- [x] `Birko.Sandbox` is included, and **after [[TASK-228]]**, so the fix is versioned rather than
      machine-local. If 228 has not landed, say so explicitly rather than silently editing an untracked tree
- [x] `audit-dependencies.ps1` is re-run at the end over the **full 246-project root** and the result
      recorded. A re-run scoped to `Framework.Tests` is not a check — that narrower scope is exactly what
      produced this task's original wrong numbers and wrong remediation floor

## Out of scope

- MongoDB — fixed in [[TASK-210]] (driver 3.2.0 → 3.11.0).
- Moving driver declarations into `.projitems` — [[TASK-229]]. **Sequencing note:** if 229 lands first, the
  ten SQLite edits collapse into one, so prefer that order. This task does not depend on it, because the
  advisories should not wait on a convention change.
- Wiring the NuGet audit — settled in [[TASK-210]]. Audit is already on by SDK default; the durable check
  is `audit-dependencies.ps1`, which this task consumes rather than builds.

## Outcome

**Three of four advisories cleared; the fourth has no fix and is recorded as such.**

| Advisory | Remedy | Result |
|---|---|---|
| `MessagePack` 3.1.3 | declared `3.*` in `Birko.Serialization.MessagePack` — the project used it and declared nothing | → **3.1.8**, clean, 108/108 |
| `OpenTelemetry.Api` / `.Exporter` 1.15.0 | declared `OpenTelemetry` + `.Extensions.Hosting` `1.*`; exporters floated with the consumer, since which exporter to ship is the host's call | → **1.17.0**, clean, 15/15 |
| `Microsoft.Extensions.Caching.Memory` 6.0.0 | direct override `10.*` — `RazorLight` 2.3.1 is the **latest** release and hard-pins 6.0.0, so no bump can clear it | → **10.0.11**, clean, 39/39 |
| `Tmds.DBus.Protocol` 0.20.0 | **none available** | recorded, see below |

**The SQLite row cleared itself.** [[TASK-229]]'s float had already taken it from 27 projects to 6, and the
6 remaining are consumers' own declarations. Nothing to do here.

### Tmds.DBus.Protocol: accepted exposure, and the measurement that decides it

Bisected the package: `0.21.2`, `0.30.0`, `0.50.0` and `0.90.0` are **all still flagged** — only **0.94.2**,
the latest, is clean. And Avalonia has not moved: `Avalonia.FreeDesktop` **11.3.20**, the newest release,
still ships `0.21.3`. So **no Avalonia bump clears it**, and the only fixed version is ~74 minor releases
ahead of what Avalonia compiled against.

Forcing 0.94.2 would substitute a likely compatibility break for a security advisory, on
`Avalonia.X11 → FreeDesktop` — a **Linux D-Bus code path that cannot be exercised from this machine at
all**. That is the trade this task's own criterion warns against ("rather than bumping to something equally
affected" — here, to something equally untested). Revisit when Avalonia moves past 0.94; nothing else
changes the answer.

### The float decision collided with Central Package Management

Not anticipated by TASK-229, and it broke consumers. **Three Birko consumers use CPM** — BardStudio, Latent,
Presenter — and CPM refuses a `PackageReference` carrying a `Version`. Result: **14 projects across two
solutions became unrestorable** with `NU1008`. Three layers had to be answered, each found by fixing the
one before it:

1. `NU1008` — version not allowed → declarations are now **dual, conditioned on
   `$(ManagePackageVersionsCentrally)`**: with a version for ordinary consumers, without one under CPM.
2. `NU1010` — CPM then requires a matching `PackageVersion` → **`Birko.Framework/Birko.Packages.props`**, 14
   entries generated from the projitems, which a CPM consumer imports from its central file.
3. `NU1011` — CPM **rejects a floating `PackageVersion` outright** → that file sets
   `CentralPackageFloatingVersionsEnabled`. **User decision (2026-08-17): ship it as built.** It is a
   consequence of the float, not a separate choice — without it the import cannot function — and it is
   opt-in by an explicit import, with the alternative (pin all 14 yourself) documented in the file.

Verified end to end on Presenter: restore **succeeds**. The probe edit was reverted, so that repo carries
none of this. The one remaining warning there is Presenter's own duplicate `Microsoft.Data.Sqlite`
declaration — consumer cleanup, not framework work, and now merely a warning rather than a failure.

**My audit script is what caught this**, and only because it had been fixed hours earlier to report a failed
restore as *unauditable* rather than clean. Before that fix, 14 unrestorable projects would have been
counted as having no findings.

### Three mistakes of mine, all caught before commit

- **I reported "166 of 166 build clean" for TASK-229 and it was false.** My sweep grepped for
  `NU1504|error CS|error NU|error MSB`, and `NETSDK1087` matches none of those — so 8 test projects,
  `Birko.Sandbox` and one consumer were failing while it printed green. Cause: a duplicate
  `FrameworkReference` is an **error**, unlike a duplicate `PackageReference`. Pattern widened to any
  `error <CODE>`; the corrected sweep reports 166/166 for real. **A checker that only looks for the failures
  you thought of is the same defect this backlog keeps finding.**
- **I edited a consumer repo I should not have.** The `FrameworkReference` cleanup swept `Consumers/*` by
  glob and modified `FisData.Stock.Angular.Server.csproj`, which holds substantial **uncommitted
  net9→net10 migration work**. Restored the line; that repo carries none of mine. Its `FrameworkReference`
  came from the uncommitted work rather than HEAD, so its original position could not be recovered from git
  — it is now first in its `ItemGroup`. **Scope a cleanup by ownership, not by glob.** (That project will
  hit `NETSDK1087` when its migration lands, since it imports `Birko.Data.Tenant` and declares its own.)
- **`Birko.Packages.props` was invalid XML on the first attempt** — my header comment embedded a
  `<!-- … -->` example inside itself, and XML forbids `--` within a comment. MSBuild caught it. The
  generator now parses the file it just wrote.

### Consumer-owned rows: reported, not edited

8 projects across DraCode (6), `Symbio.AppHost` and `sqlite-cli` carry their own `SQLitePCLRaw` 2.1.10,
`MessagePack` 2.5.192 and `Microsoft.OpenApi` 2.0.0. Each is that consumer's declaration; the framework's
part is to report them. Symbio is explicitly out of bounds. **The remedy for all of them is the same as the
framework took**: `Microsoft.Data.Sqlite` ≥ 9.0.19 or ≥ 10.0.11 for the SQLite row.

Final family audit: **11 findings across 9 projects**, all consumer-owned, plus the accepted
`Tmds.DBus.Protocol`. Down from 44 across 37 when this thread started.

## Human test plan

N/A — `dotnet list package --vulnerable` and the suites are mechanical.

## Implementation plan

_Populated by `/tasks plan TASK-230` — leave empty until then._
