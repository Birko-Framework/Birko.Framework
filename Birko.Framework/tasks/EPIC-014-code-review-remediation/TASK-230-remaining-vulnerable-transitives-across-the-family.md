---
id: TASK-230
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-210, TASK-228, TASK-229]
findings: []
pr: null
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

- [ ] Every **Birko-owned** row above is cleared or has a recorded reason it cannot be, verified by
      re-running `audit-dependencies.ps1`, not by reading nuspecs
- [ ] The **consumer-owned** rows are reported to those repos and tracked there, not silently fixed here.
      `Symbio` in particular is out of bounds for this task. Record where each was reported
- [ ] SQLite lands on a version resolving `SQLitePCLRaw.lib.e_sqlite3` **≥ 2.1.12** — the check is the
      resolved transitive, not the top-level number, since 10.0.0 > 9.0.19 and is *worse*
- [ ] `MessagePack`, `Microsoft.Extensions.Caching.Memory` and the OpenTelemetry pair each get a remedy or
      a recorded reason; where no fixed version exists, say so and record the exposure rather than bumping
      to something equally affected
- [ ] Each affected suite still passes after its bump — a package bump is exactly the change that breaks a
      test while the build stays green
- [ ] `Birko.Sandbox` is included, and **after [[TASK-228]]**, so the fix is versioned rather than
      machine-local. If 228 has not landed, say so explicitly rather than silently editing an untracked tree
- [ ] `audit-dependencies.ps1` is re-run at the end over the **full 246-project root** and the result
      recorded. A re-run scoped to `Framework.Tests` is not a check — that narrower scope is exactly what
      produced this task's original wrong numbers and wrong remediation floor

## Out of scope

- MongoDB — fixed in [[TASK-210]] (driver 3.2.0 → 3.11.0).
- Moving driver declarations into `.projitems` — [[TASK-229]]. **Sequencing note:** if 229 lands first, the
  ten SQLite edits collapse into one, so prefer that order. This task does not depend on it, because the
  advisories should not wait on a convention change.
- Wiring the NuGet audit — settled in [[TASK-210]]. Audit is already on by SDK default; the durable check
  is `audit-dependencies.ps1`, which this task consumes rather than builds.

## Human test plan

N/A — `dotnet list package --vulnerable` and the suites are mechanical.

## Implementation plan

_Populated by `/tasks plan TASK-230` — leave empty until then._
