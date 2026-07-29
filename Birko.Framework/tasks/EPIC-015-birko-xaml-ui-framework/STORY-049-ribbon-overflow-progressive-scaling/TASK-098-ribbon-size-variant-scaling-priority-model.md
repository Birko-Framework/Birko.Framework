---
id: TASK-098
parent: STORY-049
feature: null
status: todo  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-07-29
depends-on: []
blocks: [TASK-099, TASK-100]
pr: null
github-issue: null
jira-key: null
---

# Ribbon model + tokens: size variant, scaling priority, group icon (XAML **and** web together)

## Context

The Office overflow model needs two concepts neither ribbon model can express today, plus one missing
field:

- `RibbonGroup` / `RibbonItem` in `Birko.Xaml.Core/Ribbon/RibbonModels.cs:4-21` — `Id`, `Label`,
  `Icon`, `Run`, `Items`. No size, no priority.
- The TS interfaces in `b-ribbon.ts:22-38` — `RibbonGroup` has `id`, `label`, `items`. Same gap. Note
  `RibbonGroup` has **no icon** on either side, which the `Popup` variant's chunk button needs.

This task is **model + tokens only, no behaviour change.** It exists as its own task because of the
family's XAML↔web parity rule: the two models must be designed **once** and extended in the same
change, not retrofitted on one side. Getting this wrong is how the ribbon ends up with two divergent
scaling vocabularies that can never be reconciled.

**Semantics to lock down (and document, because they are ours, not Microsoft's).** Define
`ScalingPriority` as **importance**: a *lower* priority group degrades *first*, so the hero group
(Clipboard/Font) keeps large icons longest. Do not assume RibbonX's numeric direction — state Birko's
own convention explicitly in the XML doc comment and the TSDoc so nobody has to guess or go read
Microsoft docs to read our code.

Defaults must preserve today's rendering exactly: every group defaults to the largest variant and to
equal priority, so an existing consumer that sets neither field renders byte-identically. That is what
makes this a safe no-behaviour-change landing.

Tokens go through `Birko.DesignTokens/tokens.json` → generated CSS + AXAML, **never hand-authored on
one side** — `CssParityTests` and `AxamlParityTests` already gate that, and CHANGELOG records that
hand-editing generated CSS is exactly how the last drift happened. Only add tokens the variants
actually need (variant icon sizes, chunk-button width, popup padding); resist inventing a full set
before TASK-099/100 prove what's used.

## Acceptance criteria

- [ ] A size-variant enum exists in `Birko.Xaml.Core` covering `Large` / `Medium` / `Small` / `Popup`,
      with the rendering of each documented in its XML doc comment.
- [ ] `RibbonGroup` (Core) gains a scaling priority and a variant **floor** (a group that must never
      degrade past a given variant), both optional with defaults that preserve current rendering.
- [ ] `RibbonGroup` (Core) gains an `Icon` for the `Popup` chunk button.
- [ ] The TS `RibbonGroup` interface in `b-ribbon.ts` gains the **same** three fields with the same
      names (camelCased) and the same documented semantics.
- [ ] `ScalingPriority`'s direction (lower degrades first = less important) is documented in **both**
      the XML doc comment and the TSDoc, and stated as a Birko convention rather than attributed to
      RibbonX.
- [ ] Any new `--b-ribbon-*` tokens are added to `tokens.json` and regenerated — generated CSS and
      AXAML are **not** hand-edited. `CssParityTests` + `AxamlParityTests` green.
- [ ] A test pins the no-behaviour-change guarantee: a ribbon built with none of the new fields set
      renders the same tabs/groups/items as before (extend `RibbonTests.cs`).
- [ ] `Birko.Xaml.Core` stays **Avalonia-free** (EPIC-015 constraint #1 — no `using Avalonia.*`).
- [ ] The new fields are recorded wherever the ribbon's API is documented on the web side
      (`Birko.Web.Components/API.md`).

## Out of scope

- **Consuming** the new fields — no measuring, no degrading, no popup. TASK-099 and TASK-100.
- Removing the interim groups-row scroller from TASK-097 — TASK-099 does that.
- `RibbonItem`-level size overrides. Office scales at *group* granularity; per-item sizing is a
  different feature and would multiply the search space in TASK-099's measure pass for no known need.
- KeyTips / accelerator metadata on the model — separate shell-wide keyboard story.

## Human test plan

N/A — fully covered by automated tests. This task adds fields and tokens with no visible behaviour
change; the parity suites plus the no-change regression test cover it. The visible behaviour is
verified by hand in TASK-099 and TASK-100.

## Implementation plan

_Populated by `/tasks plan TASK-098` — leave empty until then._
