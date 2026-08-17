---
id: TASK-228
parent: EPIC-013
feature: FEATURE-013
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-17
depends-on: []
blocks: [TASK-229]
related: [TASK-037, TASK-210]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `Birko.Sandbox` is not a git repository — the smoke harness and the only dependency manifest exist on one disk

## Context

Found while tracing the dependency-declaration inconsistency behind [[TASK-210]]. This epic's own subject —
the integration smoke harness extracted by [[TASK-037]] — **is not under version control at all.**

Measured 2026-08-17 across `C:\Source\Birko\Consumers`: **15 of 16 consumers are git repos.
`Birko.Sandbox` is the one that is not** — `git rev-parse` reports *not a repository (or any parent)*, so it
is not nested inside one either. It exists only on this machine, and nothing versions it.

Two things are lost with it, and the second is the one that makes this P1 rather than tidy-up:

- **The harness itself.** `README.md` § *Usage in Consumer Solutions* points readers at it as *"the runnable
  integration smoke harness … the **first test place** for framework changes"*. Anyone who clones the family
  cannot run the thing the documentation tells them to run first. It is a documented entry point that ships
  to nobody.
- **The only complete manifest of the framework's external dependency surface.** Its aggregator declares
  **17 packages** — `Npgsql`, `NEST`, `Elasticsearch.Net`, `StackExchange.Redis`, `MQTTnet`, `MessagePack`,
  `protobuf-net`, `System.IO.Ports`, `Microsoft.Data.Sqlite`, the four OpenTelemetry packages,
  `Microsoft.AspNetCore.Authentication.JwtBearer`, `RazorLight`, `Newtonsoft.Json` — and no other single
  place in the family enumerates them. [[TASK-229]] needs exactly that list, which is why this blocks it.

**The README also describes it inaccurately**, which is worth correcting in the same pass rather than
filing separately: it says the *"app csproj directly imports the lean slice it exercises"*. In fact
`Birko.Sandbox/Birko.Framework/Birko.Framework.csproj` is a full **aggregator importing 165 `.projitems`** —
effectively the entire framework, not a lean slice. Whether that is a drift to fix or a description to
update is part of this task's first criterion.

**It also has four live advisories of its own**, none of which any sweep would have caught, because every
existing sweep targets `Framework.Tests`:

| Package | Severity |
|---|---|
| `MessagePack` 3.1.3 | High |
| `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (via `Microsoft.Data.Sqlite` 10.0.0) | High |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.0 | Moderate |
| `OpenTelemetry.Api` 1.15.0 | Moderate |

The 2.1.11 there is load-bearing for [[TASK-230]]: it proves the SQLite advisory covers **2.1.11 as well as
2.1.10**, so the remediation floor is 2.1.12 and not merely "newer than 2.1.10".

## Acceptance criteria

- [ ] `Birko.Sandbox` is a git repository with its sources committed, `.gitignore` covering `bin/`, `obj/`
      and any local run artefacts, matching how the other 15 consumers are set up
- [ ] Decide and record whether it stays a **consumer** (its own repo under `Consumers/`, like the other 15)
      or is really part of the framework's own test surface given the README calls it the first test place —
      a one-line rationale, because the answer changes who is expected to keep it green
- [ ] `README.md`'s description matches reality: either the aggregator is trimmed to the "lean slice" the
      text claims, or the text is corrected to describe a 165-projitems aggregator. **Do not leave the two
      disagreeing** — a reader following the current text builds something different from what is here
- [ ] `dotnet run` still exits non-zero on failure and zero on success after the move, verified by running
      it, not by inspection
- [ ] The 17-package manifest is preserved verbatim in the commit, since [[TASK-229]] consumes it

## Out of scope

- Fixing Sandbox's four advisories — [[TASK-230]] owns those, and they should be fixed *after* this task so
  the fix is versioned rather than machine-local.
- Moving the driver declarations into `.projitems` — [[TASK-229]].
- The other 15 consumers, all of which are already tracked.

## Human test plan

- [ ] From a clean clone of the family on a machine that has never had `Birko.Sandbox`, run the harness and
      confirm it executes. This is the whole point of the task and it cannot be checked on the machine that
      already has the untracked copy.

## Implementation plan

_Populated by `/tasks plan TASK-228` — leave empty until then._
