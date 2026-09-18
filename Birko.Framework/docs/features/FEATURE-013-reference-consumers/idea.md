---
id: FEATURE-013
created: 2026-06-18
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Reference consumers — integration smoke harness + Web playground

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-013](../../../tasks/EPIC-013-reference-consumers/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

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

## Proposed shape

- ✅ TUI demo removed; `Birko.Framework.csproj` is a clean all-`.projitems` compile gate (relocated into the Birko.Sandbox consumer; framework repo is docs-only)
- ✅ Backend smoke-harness consumer exists as a sibling checkout, builds via `$(BirkoSrc)`, runs a smoke pass green (Birko.Sandbox — 6/6)
- Web playground consumer exists as a sibling checkout, renders the full `b-*` catalogue, edits all `--b-*` tokens live, and exports a valid `[data-theme]` / `tokens.css` override
- Both new consumers follow the aggregator pattern from `README.md` § "Usage in Consumer Solutions"
- Docs updated (framework `README.md` / `CLAUDE.md`; new consumers carry their own `README.md` / `CLAUDE.md`)

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Framework, Birko.Web.Components, Birko.Web.Shell, Birko.Web.Core]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
