---
id: TASK-461
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-457, TASK-458]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Sandbox covers every reachable area, and reports it readably

## Context

`Birko.Sandbox` is the framework's only **consumer-side** check: it imports `.projitems` through
`$(BirkoSrc)` exactly as the wiki tells a reader to, so it catches the failure a unit suite cannot —
the framework no longer composing from outside. It had drifted to **9 imports and 8 checks**, which
covered configuration, InMemory stores, serialization, workflow, jobs and the AI factory, and
nothing else. It also printed a flat `OK`/`FAIL` list with no grouping and no timing, and — measured
— **CI never ran it at all**: the `.slnx` registers only the compile-gate aggregator, and the harness
project is not in the solution.

Two decisions shaped the work:

- **A backend needing a server is reported, not skipped.** A skip is indistinguishable from a check
  nobody wrote. `CFG` means *configuration and wiring verified without contacting anything* — the
  settings compose a connection string, the client keeps its configuration, the request builder
  produces the right request. That is the honest maximum on a bare machine and it is still the half
  that breaks when the framework moves underneath a consumer; the live suites cover the rest.
- **Only `FAIL` counts.** A machine without PostgreSQL is not a broken framework, and reporting it as
  one trains everybody to ignore the output — the same defect § Conventions records for a diagnostic
  channel nobody reads.

## What changed

**Coverage: 8 checks → 25** (19 verified, 6 wiring-only, 0 failed), imports **9 → 38 of 178**.

New: `Money` value object; JSON and XML file stores; SQLite CRUD, decimal precision and the
whole-table-write refusal; a SQL migration through `SqlMigrationRunner`; the `StoreWrapperBuilder`
decorator chain stamping from an injected clock; `MemoryCache` get/set/get-or-set; the health-check
runner; PostgreSQL/MySQL/SQL Server settings wiring; REST client wiring; GraphQL request building.

**`Harness.cs` is new** and owns the report: grouped by layer, coloured (suppressed when output is
redirected, so a CI log gets no escape codes), per-check timing, a summary line, and a failures
block at the end. Exit code is the failure count.

**CI now runs it**, ahead of the 167-suite loop — it takes ~4 s and fails on a different axis, so it
is the cheapest signal in the job.

## Results worth keeping

- **The harness found a real constraint on its first full run.** `System.Xml.Serialization` refuses
  a non-public type outright (*"Only public types can be processed"*), so `SandboxEntity` had to
  become `public`. It worked on every other backend and failed only there. The reason is now a
  `<remarks>` on the type rather than a mystery for whoever next writes `internal`.
- **The wiki's claim was overstated and is corrected.** `Home.md` said *"You cannot hand-pick a few
  projects"* and pointed everyone at the 163-import compile gate. The harness reaches ten layers
  with **38**, so the absolute form was false. Both pages now bracket the range (38 / 163) and say
  *start from a list that compiles and delete*, which is the part that was actually true.
- **Only 28 of 173 `.projitems` declare a `PackageReference`** — the ones needing an external driver.
  The csproj comment claimed "most" do. Corrected; it matters because a consumer has to supply the
  rest, which is why `Birko.Security` needed `Microsoft.Extensions.Logging.Abstractions` declared by
  hand until the closure grew enough to pull it transitively.
- **`Birko.Data.Composition` needs `Birko.Data.EventSourcing`** and nothing said so; found by
  building.

## Out of scope

- **`Birko.Data.XML/Stores/AsyncXmlSeparateStore.cs:153` warns CS8602** (possible null dereference).
  Pre-existing framework code, surfaced only because the Sandbox now imports XML, and § Code Style
  forbids nullable warnings. Not fixed here — it is a framework change, not a harness one.
  → [[TASK-462]]
- **~~Tagging has no runnable implementation to check.~~** ⚠ **Wrong, and the error was mine.**
  `TagServiceBase` exists (`Birko.Data.Tagging/Services/TagService.cs`), with all twelve hooks the
  README names, 20 passing tests and a live Symbio consumer. I had excluded tagging on the strength
  of a truncated `ls` and a misread `wc -l` — see [[TASK-463]], cancelled, for how both slipped past.
  The check was added afterwards, so the Sandbox does cover tagging.
- **Transports needing a server** (gRPC, SSE, WebSocket, SOAP) are covered by the framework's own
  live suites; REST and GraphQL are here because they are `HttpClient`-only and their wiring is
  checkable with nothing running.
