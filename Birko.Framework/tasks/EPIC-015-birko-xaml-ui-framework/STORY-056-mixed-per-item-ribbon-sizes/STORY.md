---
id: STORY-056
parent: EPIC-015
status: planned
created: 2026-07-31
affects: [Birko.Xaml.Core, Birko.Xaml.Avalonia, Birko.Web.Components, Birko.DesignTokens]
---

# Mixed per-item size variants within one ribbon group

## User story

As an application author, I want one ribbon group to mix control sizes — a single large button beside a
column of three small ones — so my ribbon can read like Office's Clipboard group (big **Paste**, stacked
**Cut / Copy / Format Painter**) instead of forcing every item in a group to the same size.

## Why Birko cannot do this today

Size is a **per-group** property on both skins, uniform across the group's items:

| | Where | Shape |
|---|---|---|
| Web | `b-ribbon.ts` | `_renderGroup(tabId, group, size, …)` stamps `size-*` on the group wrapper; `_renderItem(tabId, groupId, item)` **takes no size at all**. `RibbonItem` is `id/label/icon/href/action/badge/disabled/active/variant` — no size. |
| XAML | `Birko.Xaml.Core/Ribbon/RibbonModels.cs` | `RibbonItem` is `Id/Label/Icon/Run` — no size. |

`RibbonGroupSize` is a group-level enum on both sides.

## This is not a redo of STORY-049

Say so plainly, because the two look alike from the outside. STORY-049 modelled Office's
`SizeDefinition` deliberately as a **uniform per-group variant** — "Large: one item per column /
Medium: three items stacked per column" — and shipped the degrade ladder on that basis. Real Office
`SizeDefinition` allows **mixed layouts within a group**. This story is the extension STORY-049
simplified away, not a correction of it. STORY-049's ladder, tokens and parity gates are the
foundation this builds on and must keep passing.

## Constraints this story inherits

1. **Both skins, one design.** The model change is designed once and lands in `RibbonModels.cs`
   **and** `b-ribbon.ts` in this story — never retrofitted onto one side later. Gated by
   `AxamlParityTests` / `CssParityTests`.
2. **The real blocker is the scaling model, not the CSS.** See below.
3. **CSS is the easy half, and half-built.** `size-medium`/`size-small` already use
   `grid-auto-flow: column` with `grid-template-rows: repeat(3, auto)` — that *is* Office's "three
   stacked per column". A mixed group is one large column followed by a 3-row column, which that grid
   can already express.
4. **New tokens go through `Birko.DesignTokens/tokens.json`** → generated CSS + AXAML. Never
   hand-author on one side. (The 2026-07-29 drift, where generated CSS had been hand-edited and
   `generate` would have silently deleted it, is why this is a hard rule.)
5. **Panel height ripples.** Mixed item sizes make required panel height depend on the **tallest
   column**, not on the group variant. Consumers must size `--b-ribbon-panel-height` for the tallest
   variant the ribbon can *reach*, not the preferred one.

## Settled up front: per-item size is optional, and the group size is its fallback

An item's size is **nullable / omitted by default**; an item that sets nothing renders at its group's
size, exactly as today. Mixed sizes are opt-in per item.

This is settled before TASK-119 because it buys the story's hardest acceptance criterion for free:
"existing uniform-size ribbons render byte-identically" stops being something to engineer and verify
and becomes true by construction — every current consumer sets no item size, so every item resolves to
the group size and nothing about their render changes. It also keeps the group-level API primary, which
is what almost every ribbon actually wants.

**What it does not settle — and in fact sharpens — is what the group size *means* during degradation.**
Once an item can override, an explicit `large` item in a group degrading `Large → Medium` has three
possible readings, and the ladder behaves very differently under each:

| Reading | An explicit `large` item when the group degrades to `Medium` | Consequence |
|---|---|---|
| **Default** — group size fills in only where the item is silent | stays `large` | Degrading the group may not shrink it enough; a group could become undegradable |
| **Cap** — group size bounds the item from above | becomes `medium` | Always degradable, but "large Paste" silently disappears at narrow widths |
| **Anchor** — item size is an offset from the group's | becomes one rung below whatever the group is | Preserves the *relationship* (Paste stays bigger than the stack) at every rung |

The third preserves the design intent the feature exists for — Office's Clipboard reads as "Paste is
the big one", not "Paste is 32px" — but it is the most work and interacts hardest with the ladder.
Choosing between them is [[TASK-119]]'s core question, now stated in its sharpest form.

## The scaling model is the load-bearing problem

`ribbon-scaling.ts` and its C# mirror measure each group at **four discrete variants**:
`RibbonGroupMetrics.Widths` is `IReadOnlyDictionary<RibbonGroupSize, double>` in C# and
`Partial<Record<RibbonGroupSize, number>>` in TS — at most four numbers per group, with `widthOf()`
walking down the ladder for a missing entry.

With per-item sizes, **"width of group G at size S" stops being well-defined.** A six-item group has
many mixed configurations, and the current key cannot name them.

Reformulating that is [[TASK-119]]'s decision and it must be made **before any code is written** — it
determines the whole rewrite. Whatever replaces it inherits STORY-049's non-negotiable property:

> **The result must depend only on the arguments, never on the applied layout.**

That determinism is load-bearing, not stylistic. STORY-049 proved it the hard way — while the groups
row still had a scroller, the chevrons' hysteresis fed back into the width being scaled against, and
the same window width resolved differently depending on drag direction. The mirrored C# unit tests and
the playground's `ribbon-scaling-smoke` numeric table assert the *same* table precisely so the two
implementations cannot drift; they move together in this story too.

## Tasks

| Task | What | Depends on |
|---|---|---|
| [[TASK-119]] | **Decide** the mixed-size model — per-item degrade order vs fixed group templates | — |
| [[TASK-120]] | The model change, both skins, plus tokens | TASK-119 |
| [[TASK-121]] | Reformulate the scaling ladder for mixed groups | TASK-119, TASK-120 |
| [[TASK-122]] | Render mixed columns — CSS grid and the Avalonia panel | TASK-119, TASK-120 |
| [[TASK-123]] | Panel-height derivation under mixed sizes + extend the clipping guard | TASK-121, TASK-122 |
| [[TASK-124]] | Fix the stale `RibbonGroupSize` parity-gap comment | — (independent, do any time) |

## Acceptance criteria

- [ ] A single group can render one large item beside a column of three small ones, on **both** skins
- [ ] The mixed-size model is identical in `RibbonModels.cs` and `b-ribbon.ts`, landed together
- [ ] The degrade ladder handles mixed groups and remains a pure function of its arguments
- [ ] C# unit tests and the playground smoke assert the same numeric table
- [ ] A consumer can derive a correct `--b-ribbon-panel-height` under mixed sizes, and the rule is
      written down
- [ ] `AxamlParityTests` / `CssParityTests` green; no token hand-authored on one side
- [ ] Item size is optional; an item that sets none renders at its group's size
- [ ] Existing uniform-size ribbons render **byte-identically** — which the fallback makes structural,
      not merely tested

## Out of scope

- Office's full `SizeDefinition` XML schema. Birko needs mixed sizes, not the file format.
- Per-item `minSize` floors. Group-level `minSize` already exists; extend it only if TASK-119's
  decision forces it.
- The ribbon body scrolling. It resizes, it never scrolls — the standing rule from STORY-049, unchanged.

## Human test plan

N/A at story level — each task carries its own.
