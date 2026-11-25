---
id: TASK-119
parent: STORY-056
feature: FEATURE-015
status: done
priority: P1
assignee: human
created: 2026-07-31
depends-on: []
blocks: [TASK-120, TASK-121, TASK-122]
pr: null
github-issue: null
jira-key: null
---

# Decide the mixed-size model: per-item degrade order, or fixed group templates

## Context

**This is a decision, not an implementation.** It gates [[TASK-120]] / [[TASK-121]] / [[TASK-122]] and
determines the whole scaling rewrite, so it is worth settling deliberately rather than discovering the
answer halfway through the ladder rewrite.

Today `RibbonGroupMetrics.Widths` maps **one width per group per variant** — four numbers, keyed by
`RibbonGroupSize`. Per-item sizes break that key: a six-item group has many mixed configurations and
"width of group G at size S" no longer names one of them.

## Already settled — don't re-open it

**Per-item size is optional and the group size is its fallback.** An item that sets nothing renders at
its group's size. This is decided at story level, because it makes "existing ribbons are
byte-identical" structural rather than something to engineer: every current consumer sets no item size.

## First sub-decision: what does the group size *mean* once an item overrides it?

Answer this before the A-vs-B choice below — it constrains it.

| Reading | Explicit `large` item, group degrades `Large → Medium` | Consequence |
|---|---|---|
| **Default** — fills in only where the item is silent | stays `large` | The group may not shrink enough; potentially undegradable |
| **Cap** — bounds the item from above | becomes `medium` | Always degradable, but the "big Paste" silently vanishes when narrow |
| **Anchor** — item size is an offset from the group's | drops one rung with the group | Preserves the *relationship* at every rung |

**Anchor** best matches the intent — Office's Clipboard reads as "Paste is the big one", not "Paste is
32px" — and it keeps every group degradable, which the ladder needs. It is also the most work. Decide
explicitly; do not let it be settled implicitly by whichever is easiest to code.

Note the interaction: under **Default**, a group can be undegradable, and the ladder must then have a
defined answer (does it overflow? force `popup`? breach the item's size?). That is a real cost of that
reading and belongs in the comparison.

## The two candidates

### A. Per-item degrade order

Each item carries its own size and its own degrade priority. The pass walks items, not groups,
shrinking the least-important item first.

- **For:** maximally expressive; mirrors how the group-level `scalingPriority` already works, so the
  mental model is one idea applied at a finer grain.
- **Against:** the configuration space explodes. Width is no longer a lookup but a function of a
  per-item state vector, so the measure step must either measure many combinations or model width
  analytically. Determinism gets harder to hold, and harder to *test* — the current numeric table has
  four rows per group; this has as many as there are reachable vectors.
- **Risk:** the pass becomes an optimiser, and optimisers oscillate. STORY-049's boundary-oscillation
  failure was caused by exactly this class of feedback.

### B. Fixed group templates

A group declares a small set of named layouts (e.g. `all-large`, `one-large-plus-stack`, `all-medium`,
`all-small`, `popup`). The ladder degrades between **templates**, not items.

- **For:** `Widths` stays a small keyed map — the existing metrics shape survives with `RibbonGroupSize`
  swapped for a template id. The numeric table stays finite and reviewable. Determinism is preserved by
  construction, because the pass still picks from an ordered list.
- **Against:** an author who wants an unanticipated mix cannot express it without a new template.
- **Note:** this is closer to what Office actually ships — `SizeDefinition` is a *declared* layout, not
  a solver.

## What the decision has to satisfy

Judge both against these, and record why the loser lost:

- **Determinism.** The chosen configuration must be a pure function of the arguments — never of the
  applied layout. Non-negotiable; see the STORY-049 oscillation.
- **Testability.** Whatever it is, C# unit tests and the playground smoke must be able to assert the
  *same* numeric table. If the table cannot be enumerated, the two implementations will drift and
  nothing will catch it.
- **Both skins.** It must be expressible in `RibbonModels.cs` and `b-ribbon.ts` with the same shape.
- **Additive.** Existing uniform-size ribbons must keep rendering byte-identically.
- **`minSize` still means something.** Group-level `minSize` is a preference that may be breached
  least-important-first rather than let the row overflow. Say how that survives.

## Acceptance criteria

- [x] **Default / cap / anchor** decided and recorded, with the reasoning — this constrains everything
      below it
- [x] If **default** is chosen, the undegradable-group case has a defined answer
- [x] One model chosen, written up in this file with the reasoning
- [x] The rejected option is recorded with *why* — so it is not re-proposed later as if unexplored
- [x] An item that sets no size resolves to its group's size, in the chosen model
- [x] The chosen model is shown to satisfy each bullet above, determinism first
- [x] The replacement for `RibbonGroupMetrics.Widths` is sketched concretely enough that TASK-121 can
      start from it (the key type, and how a width is obtained for a candidate configuration)
- [x] Worked example: Word's Clipboard group — big Paste beside stacked Cut/Copy/Format Painter —
      expressed in the chosen model, and its degrade path down to `popup` traced
- [x] Confirmed the model does not require the scaling pass to read the applied layout

## Decision (the user, 2026-11-25)

Picked by the user from a side-by-side preview of the options (default / cap / anchor, then the two models), both
times the recommended one. Recorded in FEATURE-015 as D13 and D14.

### 1. The group size is an **anchor**

An item that sets a size keeps its **offset** from the group's starting step, and moves with the group at every step.
Sizes are ranked Large > Medium > Small; Popup is a group-level step, not an item size.

- The offset is measured against the group's **starting step**: the step the ladder starts it at (the ribbon's
  preferred step, Medium by default, made roomier by the group's `MinSize` floor, exactly as `Resolve` starts today).
  `Paste = large` in a group that starts at Medium is "one step bigger than the rest".
- At step S an item renders at `clamp(S shifted by its offset, Large .. Small)`. At the top the relationship can
  flatten (Paste at +1 in a Large group is still Large); at the bottom every item reaches Small, so the group can
  always go to its next step and finally to Popup.
- **An item that sets no size has offset 0**: it renders at the group's step, exactly as today.

*Why:* it keeps the author's intent ("Paste is the big one") at every step, and every group stays degradable, so the
ladder never meets a group it cannot shrink.
*Rejected:* **default** (the item size always wins) lets a group become impossible to shrink and then needs a rule
for that case (overflow, force Popup, or break the item size); **cap** (the group size is a ceiling) makes the big
Paste vanish at the first narrowing, as if it had no size at all.

### 2. The model: **one layout per step (templates)**

A group still degrades through the same four steps, Large → Medium → Small → Popup. Each step has **one** item
layout:

- **generated** by the anchor rule above, unless
- the group **declares** a layout for that step (a template), which then wins. This is how Office's "Paste stays
  Large while Cut / Copy / Format Painter shrink" is written; Office's own `SizeDefinition` is the same idea, a
  declared layout rather than a solver.

*Rejected:* **per-item degrade order** (each item has its own size and shrink priority, and the pass shrinks one item
at a time). A group's width would depend on every item's state, up to 3^n combinations to measure and to test
identically in C# and the web, and the pass becomes an optimiser, the class of feedback that caused STORY-049's
boundary oscillation. An author who needs a mix the anchor rule does not give declares a template for that step.

### How it meets the requirements

- **Determinism.** A step's layout is a pure function of (the step, the items' sizes, the group's templates), and
  `RibbonScaling.Resolve` still chooses steps from the measured widths alone. Nothing reads the applied layout.
- **Testability.** Still four widths per group, so C# tests and the Playground smoke assert the same finite table.
- **Both skins.** The same shape in `RibbonModels.cs` and `b-ribbon.ts`: an optional size per item, an optional
  map from step to per-item sizes per group.
- **Additive.** No item size and no template: every item has offset 0, every step's layout is today's, so existing
  ribbons render byte-identically.
- **`MinSize`** keeps its meaning, a floor on the group's **step** (preferred, breached least-important-first rather
  than overflowing, as now); it says nothing about individual items.

### The replacement for `RibbonGroupMetrics.Widths`

None is needed: the key stays `RibbonGroupSize` (the step) and the value stays one width. What changes is what the
renderer measures for each step: the step's **resolved layout** (generated or declared) instead of "every item at
that size". So TASK-121 (the ladder) is expected to be small: `Resolve` keeps its shape; the work is resolving a
step's layout (TASK-120), and measuring and drawing mixed columns (TASK-122).

```text
RibbonItem.Size?            : Large | Medium | Small     (null = offset 0)
RibbonGroup.Layouts?        : { [step]: { [itemId]: size } }   (declared templates, any subset of steps)
LayoutAt(group, step)       = Layouts[step] ?? anchor(group, step)
Widths[step]                = measure(LayoutAt(group, step))       // still 4 numbers per group
```

### Worked example: Word's Clipboard group

Items Paste, Cut, Copy, Format Painter; the ribbon starts groups at Medium. Anchor only, `Paste = Large`:

| Step | Paste | Cut / Copy / Format Painter |
|---|---|---|
| Medium (start) | Large (one column, label below) | Medium (stacked, labels) |
| Small | Medium | Small (stacked, icons only) |
| Popup | the group is one button; its flyout shows the items at Large | |

Office keeps Paste Large one step longer; with a declared template for Small (`Paste = Large`, the rest Small) the
path is Medium {Paste L, rest M} → Small {Paste L, rest S} → Popup. Each row is one measured width, so the ladder
degrades it exactly as it degrades a uniform group today.

## Out of scope

- Writing any of it. This task ends at a decision.
- Token names and CSS. TASK-120 / TASK-122.

## Human test plan

N/A — a design decision. Its output is the write-up in this file, which TASK-121 is then measured against.
