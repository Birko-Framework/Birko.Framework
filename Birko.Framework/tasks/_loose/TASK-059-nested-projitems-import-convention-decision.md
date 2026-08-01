---
id: TASK-059
# parent deliberately null: this is a decision ticket living in _loose/. Declaring a parent while
# sitting here makes it render in two places on the dashboard. Related to EPIC-016 in substance;
# see the body. Same convention as TASK-106 and TASK-127.
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-07-14
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Decide the long-term convention for nested `.projitems` imports (MSB4011)

Substantively this belongs to **EPIC-016** (framework backports from Reps), but `parent` is left null on
purpose: the ticket lives in `_loose/` because it is a framework-wide convention decision rather than work
on any one project, and declaring a parent from here makes it render in two places on the dashboard. Same
convention as [[TASK-106]] and [[TASK-127]]. Move it under the epic directory if that is ever reversed.

## Context

Surfaced as **item #5** of the BardStudio backports prompt
(`Consumers/BardStudio/docs/framework-backports-prompt.md`). Consumers using the aggregator pattern
(e.g. `Symbio.Birko`, `BardStudio.Birko`) that import the full dependency closure hit MSB4011
duplicate-import warnings because three shared projects **nest-import** their own dependencies:

- `Birko.Data.Stores.projitems` → `Birko.Configuration.projitems`
- `Birko.Data.Core.projitems` → `Birko.Contracts.projitems`
- `Birko.Time.projitems` → `Birko.Time.Abstractions.projitems`

These nested imports are the framework's **documented architecture** (root `CLAUDE.md` dependency
flow), so the prompt's proposed "just remove the nested imports" fix would reverse a deliberate
design decision — not a clear-cut bug.

## Interim resolution (SHIPPED 2026-07-14 — the "middle option")

Added an **include-guard sentinel** to each of the three leaf projitems and conditioned the nested
import in each parent on it:

- Leaf sets e.g. `<BirkoConfigurationProjitemsImported>true</...>` in its first `PropertyGroup`.
- Parent's nested import gains `Condition="'$(BirkoConfigurationProjitemsImported)' != 'true'"`.

Result: an aggregator that lists the full closure (leaves before dependents — the natural order)
no longer double-imports → **MSB4011 gone**; a project that imports only a parent still gets the
leaf via the (now-guarded) nested import. Verified: `Birko.Data.Migrations.SQL.Tests` (imports the
full closure) builds with **0 MSB4011**; `Birko.Data.SQL.Tests` (relies on the nested closure)
still resolves and both suites stay green (34 / 289).

**Known limitation:** the guard only protects the framework-internal nested imports. A consumer's
own aggregator imports are unconditioned (we can't edit consumer files), so a consumer that lists
its closure in *reverse* dependency order (a dependent before its leaf) could still trip MSB4011.
That is a consumer ordering issue, and the guard strictly improves on the prior state.

## The decision still to make (future)

Pick the permanent convention:

1. **Keep the guarded-nested-import approach (current).** Aggregators keep the free transitive
   closure; the sentinel silences the warning. Con: a small amount of MSBuild machinery in every
   leaf; reverse-order consumer imports still warn.
2. **Flip to "shared projects never import other shared projects."** Remove all nested projitems
   imports; every consumer aggregator must enumerate the full transitive closure itself. Con:
   verbose aggregators, and a missed dep becomes a compile error instead of a warning. Pro: no
   MSBuild guard machinery; matches the prompt's suggested convention. If chosen, update the root
   `CLAUDE.md` dependency-flow section (register-on-introduce) and grep **all** `*.projitems` for
   any remaining nested `<Import …projitems`.

## Acceptance (when the decision is taken)

- The chosen convention is recorded in root `CLAUDE.md` § Conventions.
- If option 2: all nested projitems imports removed; `dotnet build` of at least one aggregator
  consumer (Symbio or BardStudio) shows no MSB4011 and no missing-type errors.
- If option 1: this task is closed as `cancelled`/`done` with the interim resolution made permanent.

## Notes

- Interim work touched: `Birko.Configuration`, `Birko.Contracts`, `Birko.Time.Abstractions`
  (sentinels) and `Birko.Data.Stores`, `Birko.Data.Core`, `Birko.Time` (guarded imports).
- Related backport batch: the other six BardStudio fixes (SQL `Contains`, Avalonia Button/TabItem/
  ComboBox, `SqlScriptMigration`, `Message.GetText`) shipped in the same pass — see root
  `CLAUDE.md` Recent Updates "BardStudio consumer backports (2026-07-14)".
