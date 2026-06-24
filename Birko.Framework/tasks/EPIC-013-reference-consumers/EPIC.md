---
id: EPIC-013
status: in-progress
created: 2026-06-18
owner: ai
affects: [Birko.Framework, Birko.Web.Components, Birko.Web.Shell, Birko.Web.Core]
---

# Reference consumers — integration smoke harness + Web playground

## Area of concern

The framework has no first-class **consumer** to dogfood itself. Today the only
runnable artifact is the dual-purpose `Birko.Framework\Birko.Framework\` project: it is
simultaneously the all-in compile-validation aggregator (imports every `.projitems`) **and**
a `XenoAtom.Terminal.UI` TUI demo (`Program.cs`, ~47 KB). That conflation makes it neither a
good build gate nor a good example.

Split the concerns into two purpose-built reference consumers, each extracted to its own
sibling checkout (→ `Birko\Consumers\` after [[TASK-036]]):

1. A **backend integration smoke harness** — a realistic consumer (uses the aggregator +
   `$(BirkoSrc)` pattern) that wires and exercises a representative slice of every layer.
   The "first test place" you compile and run when validating a framework change.
2. A **Birko.Web playground** — a component gallery + live design-token editor that exports
   a paste-ready theme CSS, mirroring the modular `css/themes/*.css` + `registerThemes()` system.

The `Birko.Framework.csproj` aggregator is reduced to a **bare, headless compile-validation
aggregator** (imports all `.projitems`, no TUI, no demo deps) so the "all projects compile" gate
survives the extraction. **(Revised 2026-06-18:** the gate was also *relocated* out of the framework
repo into the Birko.Sandbox consumer as a second project — Option A — leaving
`Birko\Framework\Birko.Framework` as a docs/meta-only repo.)

## Success criteria

- ✅ TUI demo removed; `Birko.Framework.csproj` is a clean all-`.projitems` compile gate (relocated into the Birko.Sandbox consumer; framework repo is docs-only)
- ✅ Backend smoke-harness consumer exists as a sibling checkout, builds via `$(BirkoSrc)`, runs a smoke pass green (Birko.Sandbox — 6/6)
- Web playground consumer exists as a sibling checkout, renders the full `b-*` catalogue, edits all `--b-*` tokens live, and exports a valid `[data-theme]` / `tokens.css` override
- Both new consumers follow the aggregator pattern from `README.md` § "Usage in Consumer Solutions"
- Docs updated (framework `README.md` / `CLAUDE.md`; new consumers carry their own `README.md` / `CLAUDE.md`)

## Tasks

- TASK-037 — Replace the TUI example with an extracted backend integration smoke-harness consumer
- TASK-038 — Birko.Web playground: component gallery + live token editor + theme-CSS export
