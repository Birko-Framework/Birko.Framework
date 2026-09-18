---
id: TASK-444
# parent deliberately null: a ticket in _loose/ that declares a parent renders in two places on the
# dashboard. Same convention as TASK-059 / TASK-106 / TASK-127.
parent: null
feature: null
status: done
priority: P3
assignee: null
created: 2026-08-16
closed: 2026-09-16
depends-on: []
blocks: []
related: []
pr: 188e6e1 (Birko.Models.Contracts) + 28e0d06 + acade31 (Birko.Models.Inventory) + b919ddc (Birko.Models.Inventory.SQL) + 5039686/f2c8c0f/67222dd (tests)
github-issue: null
jira-key: null
---

# `Birko.Models.Inventory` had no stock-balance model, and `IBatchable` had no implementors

> ⚠ **The id is consumer-origin and is adopted verbatim.** This task was filed in consumer Symbio as
> `Symbio/tasks/_loose/TASK-444-birko-inventory-has-no-balance-model.md`, whose own text sanctions the
> move: *"Recorded here rather than in the framework because that is where the measurement was taken;
> move or copy it when the framework side is picked up."* It is **not** renumbered into this tree's
> sequence (max id 332 at time of writing) because `TASK-444` is already baked into six commit
> messages, three `CLAUDE.md` files, two `README.md` files and roughly a dozen XML doc comments in
> framework source — a tracking file whose id matched none of the code it tracks would be worse than
> the collision. **Reserve 444 in this tree's sequence**; when the framework counter approaches it,
> skip it rather than reuse it.

## Context

Spawned from Symbio's TASK-087. The retired `Birko.Models.Warehouse` migration recorded
`Warehouse.AbstractItemRepository → Inventory.StockMovement` as a one-to-one rename. It was not one.
`AbstractItemRepository` was an **abstract coordinate base** — `ItemGuid`, `ItemVariantGuid`,
`RepositoryGuid`, `AgendaGuid`, `Batch`, and **no quantity and no date** — with concrete descendants
supplying the payload: `ItemRepository` (+`Amount`, the **balance**), `ItemRepositoryMovement`
(+amounts, prices, VAT, document, date — the **ledger**) and `ItemRepositoryInventory` with five
period variants (start/add/remove/end amounts — a period **snapshot**).

> ⚠ **Provenance corrected 2026-09-16.** The Symbio task, `Birko.Models.Inventory/CLAUDE.md` and this
> task's own first draft all say that shape was *"recovered from `FisData.Stock.Core`'s git history"*.
> Measured: `git log --all -S "Birko.Models.Warehouse"` in that repo returns **nothing**, and no
> `*Warehouse*` file was ever deleted there — the retired framework file is **not** in its history.
> What is actually there is **stronger**: `FisData.Stock.Core` still carries the same shape as its own
> **committed, current** source — `Models/AbstractItemRepository.cs` at `HEAD` declares exactly those
> five fields on an abstract base over `AbstractDatabaseLogModel`, with `ItemRepository`,
> `ItemRepositoryMovement`, `ItemRepositoryInventory` and its Daily/Weekly/Monthly/Quarterly/Yearly
> variants beside it. The conclusion is unchanged and better evidenced; only the stated source was
> wrong. *Evidence that has to be excavated from history invites less checking than evidence sitting in
> a working file, which is presumably how the wrong provenance survived three retellings.*

Collapsing that onto one concrete movement class left the domain unable to express *"how much of this
item is here right now"*. Two consumers carry the concept the framework lost — FisData **never moved
off** the old hierarchy (its committed models still descend from its own `AbstractItemRepository`, and
reference `Birko.Models.Inventory` in **0** files), and Symbio wrote its own `StockItem` with
`QuantityOnHand`. Separately, `Birko.Models.Contracts.IBatchable`
declared the exact batch pair the domain needed and had **zero implementors** — because it declared
`BatchNumber` as non-nullable `string`, which no optional-batch entity can satisfy.

**The finding survived re-derivation; the original explanation of the mechanism did not.** The Symbio
task's first reading (a 1:2 balance/movement split) was invented from the rename table's naming system
plus two consumers' behaviour, was labelled circumstantial, and was **wrong in its specifics** — the
base was neither a balance nor a movement, and the collapse was 1:4, not 1:2. It was corrected only
because the prompt asked for it to be confirmed from source. *Write the confidence level down, and say
where the confirming evidence would live.*

## Acceptance criteria

- [x] **`IBatchable.BatchNumber` becomes `string?`.** — `Birko.Models.Contracts/Contracts/IBatchable.cs`,
      commit `188e6e1`. Both members are now documented as optional, with the reason it had no
      implementors recorded on the interface itself.
- [x] **A balance model in `Birko.Models.Inventory`.** — `Models/StockBalance.cs` +
      `ViewModels/StockBalance.cs`, commit `28e0d06`, both registered in `.projitems`. Coordinates
      item × variant × location × batch within a tenant; `Quantity` is signed on purpose. Implements
      `IBatchable`, `ILoadable<>`, `ICopyable<>`. Cost is deliberately absent (see § Decisions).
- [x] **`StockMovement.Batch` aligns to `IBatchable.BatchNumber`.** — renamed on both `StockMovement`
      and `InventoryDocumentLine`, both gained `ExpiryDate`, both now implement `IBatchable`. Three
      implementors where there were none.
- [x] **Decide whether FisData's `IBatchTracked` collapses into `IBatchable`.** — **No.** See
      § Decisions › Criterion 4. Decided 2026-09-16 on re-measurement.
- [x] **Symbio adoption stays out of scope.** — verified still true 2026-09-16: Symbio contains **0**
      `.cs` references to `Birko.Models.Inventory` types. The only reachability is two `.projitems`
      imports in `Symbio.Birko.csproj` (lines 149 and 157), which compile the models into the
      consolidated DLL; nothing reads them.

## Decisions

### Criterion 4 — `IBatchTracked` does **not** collapse into `IBatchable`

**Measured 2026-09-16 — and the measurement has to name WHICH STATE, because the two disagree.**

`FisData.Stock.Core` has a **dirty working tree**: 144 modified files and 24 untracked ones, none
touched in this session (`find -newermt "2 hours ago"` → nothing). That dirt is not noise — it is an
**in-flight, uncommitted migration of FisData onto `Birko.Models.Inventory`**, and every fact this
criterion turns on differs between the two states:

| | `HEAD` (`1340e66`, 2026-03-28) | working tree (uncommitted) |
|---|---|---|
| files referencing `Birko.Models.Inventory` | **0** | several, incl. the 3 models below |
| `IBatchTracked` | **does not exist** — `Contracts/` is not in the tree at all | declared, `Contracts/Contracts.cs:30`, **untracked** |
| `Batch` on the models | declared **locally** (`public string Batch { get; set; }`) | local declaration removed, **inherited** from the Birko base |

Verified with a positive control, because a `git grep` that finds nothing looks identical to one whose
command form is wrong: `git grep -l "namespace FisData" HEAD` → **145** files, while
`Birko.Models.Inventory` and `IBatchTracked` → **0** each.

So `IBatchTracked` is **not shipped code**. It is an untracked file in a dormant repo's work-in-progress
(last substantive commit 2025-02-07; one framework-compliance commit 2026-03-28; one downstream
consumer, `FisData.Stock.Angular.Server`). The 5-file spread cited in the Symbio task — and in the
first draft of this one — is the **working tree's**, not the repository's.

**Decision: they stay separate.** Four grounds:

1. **They are not the same contract, and TASK-444 just finished making that explicit.**
   `IBatchTracked` is a single non-nullable `string Batch` — its name and its non-nullability together
   assert *"this row **is** batch-tracked"*. `IBatchable` is an optional pair (`BatchNumber` +
   `ExpiryDate`) whose own remarks now state that it means *"batch tracking is **available** here, not
   that every row has one"*. Merging them would re-muddle the distinction the nullability fix was for.
2. **The second data point is weaker than the task assumed, because it was never committed.** TASK-087's
   constraint offered FisData as *"a second data point about what the contract should say"*. Measured,
   it is an **uncommitted draft** in a repo Symbio is intended to replace — so it is evidence of what
   one in-flight migration reached for, not of a shipped design. What it reached for was something
   **narrower** (no expiry) and **stricter** (non-null), satisfied by *inheriting* the member rather
   than implementing it. That argues FisData wanted a different shape, not that the two shapes are one.
   Symbio — the intended replacement — wanted `IBatchable`'s shape exactly and adopted it unchanged in
   its TASK-446. **The committed data point is Symbio's, and it says keep `IBatchable` as it is.**
3. **Nobody pays for the migration.** Collapsing means reconciling the FisData working tree's 5
   `IBatchTracked` files, renaming `Batch` → `BatchNumber` on two ViewModels (`AbstractItemRepository`,
   `WareHouseDocumentItem`) and their ~20 usage sites across `Repositories/` and `Services/`, and
   renaming a persisted column — in a dormant repo, on top of a migration nobody has committed.
4. **Not collapsing costs the framework nothing.** `IBatchTracked` lives entirely in the consumer, and
   not even in its repository; the framework carries no code, no shim and no obligation for it.

### ⚠ The rename breaks no committed consumer — and the first draft of this task said it did

Recorded because the error is more useful than the conclusion. The rename's commit message and
`Birko.Models.Inventory/CLAUDE.md` both justify it as *"taken deliberately while nothing in the
framework or in Symbio read either property"*. Checking whether that understates the blast radius is
the right question; the first answer was wrong, in the **unsafe** direction — it reported a shipped
consumer as broken.

**What is true.** `Batch` no longer exists anywhere in the base chain (`Birko.Data.Core` declares none;
`StockMovement` and `InventoryDocumentLine` now declare `BatchNumber`), and in FisData's **working
tree** three files reference it through inheritance — `Models/AbstractItemRepository.cs:36`,
`Models/ItemRepository.cs:13`, `Models/WareHouseDocumentItem.cs:68`. In its **repository**, none do:
those files declare their own `public string Batch` and reference `Birko.Models.Inventory` **not at
all**. So no committed consumer is broken, and the quoted justification holds with one clarification
worth having in writing: *no consumer's committed code read either property either.*

**The lesson, which is the reason this section survives at all:** `git status` was run on FisData only
to prove I had not edited it. It said 168 changes — and the obvious reading, "pre-existing dirt in a
dormant repo", was the wrong one. The dirt **was the subject**: an uncommitted migration onto the very
models this task changed. *A working tree is not a repository, and when a measurement is taken by
reading files, say which of the two was measured* — the same discipline this task's own Context section
records about the Symbio ticket's original mechanism, arriving a second time in the same task.

The two **filters** are unaffected in either state, and that is worth keeping because it is what makes
the decision cheap: `Filters/ItemRepository.cs` and `ItemRepositoryDate.cs` are generic and constrained
on `IBatchTracked`, never on `StockMovement`, so they are structurally independent of the framework.

**Nothing to fix, and therefore nothing to file.** Whoever finishes FisData's migration reconciles it
against the current models as a matter of course — that is what finishing a migration is — and they
will meet `BatchNumber` rather than `Batch`. That is criterion 4 re-opened *on FisData's own terms and
at FisData's own cost*, which is the right place for it.

### Deliberately not built

- **The abstract coordinate base is not reinstated.** One implementor does not justify it —
  `StockMovement` has different coordinates (two locations, not one).
- **The period-snapshot type is not reinstated.** Reporting, not a domain model. Symbio has a
  counterpart in `StockSnapshot` (its FEATURE-091).
- **No cost / valuation on `StockBalance`.** A balance's value depends on a costing *policy* — FIFO,
  LIFO and weighted average produce different numbers from the same movements — so it is derived, not
  stored state, and putting it on the model forces one policy on every consumer. The retired design
  agreed: `ItemRepository` carried `Amount` alone while every price lived on `ItemRepositoryMovement`.
  Reservations, min/max and reorder points are the same kind of thing and are likewise absent.
- **The old name `ItemRepository` is unusable regardless** — *Repository* means the data-access pattern
  in this framework (`Birko.Data.Repositories`).

## Out of scope

- **Symbio adopting `Birko.Models.Inventory`.** Its own task and its own risk: ~16 Warehouse
  repositories and ~8 event handlers register Symbio's own entities, with live rows to reshape and no
  migration mechanism. Owned by Symbio.
- **`FisData.Stock.Core`.** No id filed, and per § Task tracking's *"an unowned bullet that describes
  WORK gets an id"* that needs its reason stated rather than assumed: **there is no work here.** The
  committed repo does not reference `Birko.Models.Inventory` at all, so nothing shipped is broken; the
  only coupling is inside an uncommitted migration whose author reconciles against the current models
  by definition. A task would be filed against a working tree nobody else can see, in a repo with no
  `tasks/` tree. This is a boundary, not a skipped spawn.
- **Adding a `BatchNumber` length to `InventoryDocumentLineMapping`.** `StockBalanceMapping` and
  `StockMovementMapping` both set `HasPrecision(256)` on it; `InventoryDocumentLineMapping` maps only
  decimals and leaves every string unmapped, so it is internally consistent and was left alone. Noted
  so the asymmetry reads as a decision rather than an oversight.

## Verification

Re-derived from source on **2026-09-16**, not from commit messages. All six repos clean, nothing
uncommitted.

| Repo | Commit |
|---|---|
| `Birko.Models.Contracts` | `188e6e1` |
| `Birko.Models.Inventory` | `28e0d06`, + `acade31` (provenance correction made at this close) |
| `Birko.Models.Inventory.SQL` | `b919ddc` |
| `Birko.Models.Contracts.Tests` | `5039686` |
| `Birko.Models.Inventory.Tests` | `f2c8c0f` |
| `Birko.Models.Inventory.SQL.Tests` | `67222dd` |

Suites re-run green: `Birko.Models.Contracts.Tests` **15 passed**, `Birko.Models.Inventory.Tests`
**15 passed**, `Birko.Models.Inventory.SQL.Tests` **19 passed** — **49 total, 0 failed, 0 skipped**.

The tests pin the **design**, not only the code, and were mutation-verified when written:
`BatchNumber_is_nullable` asserts through `NullabilityInfoContext` (reverting to `string BatchNumber`
fails it, 1 of 15); `Carries_no_cost_or_valuation_field` fails if anyone adds `AverageCost`, forcing
them to argue against the reasoning in the model's own remarks;
`The_natural_key_is_item_variant_location_batch_within_a_tenant` asserts the **absence** of
`FromLocationGuid`/`ToLocationGuid` — a balance sits at one location, two is a movement, and that is
precisely the distinction the original migration lost.

## Human test plan

- [x] `N/A — covered by automated tests.` Every criterion is a type shape or a compile-time fact.
