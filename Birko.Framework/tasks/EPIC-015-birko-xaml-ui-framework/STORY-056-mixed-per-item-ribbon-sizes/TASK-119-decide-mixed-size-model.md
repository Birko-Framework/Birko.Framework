---
id: TASK-119
parent: STORY-056
feature: FEATURE-015
status: todo
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

- [ ] **Default / cap / anchor** decided and recorded, with the reasoning — this constrains everything
      below it
- [ ] If **default** is chosen, the undegradable-group case has a defined answer
- [ ] One model chosen, written up in this file with the reasoning
- [ ] The rejected option is recorded with *why* — so it is not re-proposed later as if unexplored
- [ ] An item that sets no size resolves to its group's size, in the chosen model
- [ ] The chosen model is shown to satisfy each bullet above, determinism first
- [ ] The replacement for `RibbonGroupMetrics.Widths` is sketched concretely enough that TASK-121 can
      start from it (the key type, and how a width is obtained for a candidate configuration)
- [ ] Worked example: Word's Clipboard group — big Paste beside stacked Cut/Copy/Format Painter —
      expressed in the chosen model, and its degrade path down to `popup` traced
- [ ] Confirmed the model does not require the scaling pass to read the applied layout

## Out of scope

- Writing any of it. This task ends at a decision.
- Token names and CSS. TASK-120 / TASK-122.

## Human test plan

N/A — a design decision. Its output is the write-up in this file, which TASK-121 is then measured against.
