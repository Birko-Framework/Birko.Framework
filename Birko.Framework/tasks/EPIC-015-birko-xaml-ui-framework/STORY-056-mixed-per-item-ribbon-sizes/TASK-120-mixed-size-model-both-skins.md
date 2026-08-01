---
id: TASK-120
parent: STORY-056
feature: FEATURE-015
status: todo
priority: P1
assignee: ai
created: 2026-07-31
depends-on: [TASK-119]
blocks: [TASK-121, TASK-122]
pr: null
github-issue: null
jira-key: null
---

# The mixed-size model, in both skins, with its tokens

## Context

Land TASK-119's chosen model in the two model files **in the same change**. This is the XAML↔web parity
rule: designed once, landed together, never retrofitted onto one side.

What exists today:

- `Birko.Xaml.Core/Ribbon/RibbonModels.cs` — `RibbonItem` is `Id` / `Label` / `Icon` / `Run`.
  `RibbonGroup` is `Label` / `Items` / `Icon` / scaling priority. `RibbonGroupSize` is the group-level
  enum (`Large` / `Medium` / `Small` / `Popup`).
- `b-ribbon.ts` — `RibbonItem` is `id/label/icon/href/action/badge/disabled/active/variant`.
  `RibbonGroupSize` is `'large' | 'medium' | 'small' | 'popup'`, declared with a comment saying it
  mirrors the C# enum and to keep the two in step.

Both must gain the same concept with the same names, differing only in casing convention.

## Approach

Whatever TASK-119 chose, the additions are: a per-item size (or the item's role within a template), and
however a group declares its permitted layouts. Keep the existing group-level `RibbonGroupSize` working
— it is the uniform case, and every current consumer uses it.

**The per-item size is optional, with the group size as fallback** — settled at story level. In C# that
is a nullable `RibbonGroupSize?` on `RibbonItem` (default `null`); in TS an optional
`size?: RibbonGroupSize`. An item that sets nothing resolves to its group's size.

Resolve the fallback in **one shared place**, not at each render site. `_renderItem` currently takes no
size at all and `Ribbon.cs:899` branches on the group's size inline — if each skin does its own
null-coalescing, the two will disagree the first time TASK-119's default/cap/anchor rule is anything
more interesting than "use the group's". Put the resolution next to the model, mirrored, and have both
renderers call it. That also gives the ladder ([[TASK-121]]) one function to ask for an item's
effective size rather than reimplementing the rule a third time.

**Any new token goes through `Birko.DesignTokens/tokens.json`** and is regenerated into CSS and AXAML.
Do not hand-author a value in a generated file: the 2026-07-29 incident, where hand-edits to generated
CSS would have been silently deleted by the next `generate`, is why `verify` gained an AXAML drift gate
and why `AxamlParityTests` now gates the generated dictionaries from the suite.

Mixed sizes probably need at most a couple of new tokens (a column gap between a large item and an
adjacent stack, perhaps a stack row height). Add only what the render step actually consumes — check
against TASK-122 before minting a token nothing reads.

## Acceptance criteria

- [ ] `RibbonModels.cs` and `b-ribbon.ts` carry the same model, added in one change
- [ ] Names match across skins (allowing for `PascalCase` vs `camelCase`)
- [ ] The `b-ribbon.ts` "mirrors `RibbonGroupSize` … keep the two in step" comment is updated to
      describe the new shape
- [ ] Group-level `RibbonGroupSize` still works unchanged — the uniform case is the default
- [ ] Per-item size is **optional**; an item that sets none resolves to its group's size
- [ ] The fallback is resolved by **one mirrored function per skin**, called by both renderers and by
      the ladder — not re-implemented at each render site
- [ ] That function is unit-tested on both sides against the same cases, including the
      default/cap/anchor rule TASK-119 chose
- [ ] Any new token is defined in `tokens.json` and reaches CSS **and** AXAML by regeneration
- [ ] `Birko.DesignTokens` `verify` passes both the CSS and the AXAML drift gate
- [ ] `AxamlParityTests` / `CssParityTests` green
- [ ] A model-level unit test constructs the Clipboard example (large Paste + stacked
      Cut/Copy/Format Painter) on both sides
- [ ] No rendering change yet — this task is the model only, and existing ribbons are byte-identical

## Out of scope

- The degrade ladder ([[TASK-121]]) and the rendering ([[TASK-122]]). This task makes the shape
  expressible; it does not make it visible.
- Panel height ([[TASK-123]]).

## Human test plan

N/A — covered by automated tests. Nothing is visible yet; the parity gates and the model test are the
whole verification surface.
