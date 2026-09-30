---
id: TASK-509
parent: null
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-30
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Split the Birko.Models domain models out of the framework into their own repo

## Context

Raised by the owner on 2026-09-30: should `Birko.Models.*` live outside the framework at all? Measured
the same day, the 15 projects are two different things, and only one of them is a candidate:

| Half | Projects | Who uses it |
|---|---|---|
| **Base layer — stays** | `Birko.Models` (Money, MoneyWithTax, Percentage, PostalAddress, Quantity, AbstractTree, ValueData), `Birko.Models.Contracts` (zero-dep interfaces: ICatalogItem, IPriceable, IAddressable, …), `Birko.Models.SQL` (the `ModelMap` / `ModelMapRegistry` mapping mechanism, no canonical mappings) | nearly every consumer — BardStudio, DraCode, Presenter, WorkoutTracker, Affiliate, Symbio |
| **Domain models — move** | `Birko.Models.Category`, `.Customers`, `.Inventory`, `.Pricing`, `.Product`, `.SEO`, `.Users`, and `.Customers.SQL`, `.Inventory.SQL`, `.Pricing.SQL`, `.Product.SQL`, `.Users.SQL` — 12 projects, plus their 12 `tests/Birko.Models.*.Tests` | Symbio (all of them), Affiliate (Product/Category/SEO), WorkoutTracker (Users + Users.SQL), FisData.Stock.Angular (Category/Customers/Users) |

**Why it is a candidate.** Nothing in the framework consumes `Birko.Models.*` — the only references outside
the Models projects are two doc comments in `Birko.Data.Views` (`IViewMapping.cs:5`, `ViewMapRegistry.cs:10`)
naming it as the pattern they follow. The dependency points one way (Models → `Birko.Data.Models`,
`Birko.Data.Filters`, `Birko.Data.Patterns.Schema`), so the domain half is an opinionated e-commerce /
back-office vocabulary layered *on* a general-purpose framework — the same relationship `Birko.Web` has, and
`Birko.Web` already lives in its own repo (`Birko-Framework/Birko.Web`, cloned as the `Birko/Web` bucket).

**The cost that decides whether it is worth it: breaking-change detection.** Today a change to
`Birko.Data.Core` is compiled against the domain models and their tests by the framework build. In a separate
repo it is not, and a `.projitems` shared project has **no package identity**, so a break carries no signal —
that property is exactly how DraCode's 41 tools went uncompiled for 2½ months ([[TASK-483]]). The split is
only safe with a CI job in the new repo that checks out framework `main` and builds + tests against it **on a
schedule**, not only on the new repo's own commits.

**Naming is open.** If both halves keep `Birko.Models.*`, the prefix stops telling you which repo a project is
in. Renaming the domain half (e.g. `Birko.Domain.*`) is cleaner but changes every consumer `.csproj` import
and every `using`. Decide before moving anything.

**Found while measuring, not caused by this task:** `FisData.Stock.Angular.Server.csproj` imports
`Birko.Models.Accounting` and `Birko.Models.Warehouse`, neither of which exists — that consumer is already
stale.

## Acceptance criteria

- [ ] Naming decided (keep `Birko.Models.*` or rename the domain half) and recorded, with the reason
- [ ] New repo `Birko-Framework/<name>` holds the 12 domain projects and their 12 test projects, with
      history carried over (not a flat copy)
- [ ] The new repo's CI checks out `Birko-Framework/Birko.Framework` into the dev-machine layout and builds +
      tests against framework `main`, on push **and** on a schedule
- [ ] Base layer (`Birko.Models`, `.Contracts`, `.SQL`) and its tests stay in the framework and still build green
- [ ] Framework registrations cleaned: `.slnx`, `.code-workspace`, aggregator `.csproj`, `CLAUDE-projects.md`
      catalog + Dependency Flow, `README.md` / `docs/models.md`
- [ ] Every consumer importing a moved project resolves it from the new bucket via a `$(Birko…Src)`-style
      property (no hard-coded paths) and builds: Symbio, Affiliate, WorkoutTracker, FisData.Stock.Angular
- [ ] `CHANGELOG.md` entry per [CLAUDE-maintenance.md](../../CLAUDE-maintenance.md) § Breaking changes in a
      shared project — which projects moved, where, and the consumer migration

## Out of scope

- Moving the base layer — it is general-purpose and used by nearly every consumer; it stays.
- Fixing FisData's references to the non-existent `Birko.Models.Accounting` / `.Warehouse` beyond what this
  move forces — if it is not fixed as part of the consumer migration, spawn it as its own task.

## Human test plan

- [ ] Fresh clone of the three buckets (`Framework`, `Web`, the new repo) side by side → Symbio builds and
      its tests pass with no manual path overrides
- [ ] Make a deliberate breaking change to a `Birko.Data.Core` type the domain models use, on a framework
      branch → the new repo's scheduled/dispatched CI goes red against it (proves the detection gap is closed)

## Implementation plan

_Populated by `/tasks plan TASK-509` — leave empty until then._
