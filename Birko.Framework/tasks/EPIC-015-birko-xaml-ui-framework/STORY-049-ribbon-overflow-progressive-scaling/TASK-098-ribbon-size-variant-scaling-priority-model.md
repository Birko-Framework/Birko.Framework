---
id: TASK-098
parent: STORY-049
feature: null
status: done  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
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

- [x] A size-variant enum exists in `Birko.Xaml.Core` covering `Large` / `Medium` / `Small` / `Popup`,
      with the rendering of each documented in its XML doc comment.
      Declared roomiest-first so a measure pass can compare with `<`/`>` instead of a lookup table;
      pinned by a test.
- [x] `RibbonGroup` (Core) gains a scaling priority and a variant **floor** (a group that must never
      degrade past a given variant), both optional with defaults that preserve current rendering.
- [x] `RibbonGroup` (Core) gains an `Icon` for the `Popup` chunk button.
- [x] The TS `RibbonGroup` interface in `b-ribbon.ts` gains the **same** three fields with the same
      names (camelCased) and the same documented semantics.
- [x] `ScalingPriority`'s direction (lower degrades first = less important) is documented in **both**
      the XML doc comment and the TSDoc, and stated as a Birko convention rather than attributed to
      RibbonX. Also pinned by a test, since a reader coming from RibbonX may assume the opposite.
- [x] Any new `--b-ribbon-*` tokens are added to `tokens.json` and regenerated — generated CSS and
      AXAML are **not** hand-edited. `CssParityTests` + `AxamlParityTests` green.
      Three new (`--b-ribbon-icon-large`, `--b-ribbon-icon-small`, `--b-ribbon-chunk-width`) **plus two
      pre-existing ones that were never defined** — see the extra note below.
- [x] A test pins the no-behaviour-change guarantee: a ribbon built with none of the new fields set
      renders the same tabs/groups/items as before (extend `RibbonTests.cs`).
- [x] `Birko.Xaml.Core` stays **Avalonia-free** (EPIC-015 constraint #1 — no `using Avalonia.*`).
      Guarded by the pre-existing `CoreIsAvaloniaFreeTests`, which covers this change automatically.
- [x] The new fields are recorded wherever the ribbon's API is documented on the web side
      (`Birko.Web.Components/API.md`). Also added the **missing** `Birko.Xaml.Core.Ribbon` entry to
      Core's `CLAUDE.md` § Current contents, which never listed the ribbon models at all.

## Found while doing this

**Two ribbon tokens were used but never defined.** `--b-ribbon-group-gap` and `--b-ribbon-item-gap`
appear in `b-ribbon`'s CSS with inline fallbacks but existed in neither `tokens.json` nor `tokens.css` —
so the fallback silently applied and the values *looked* tokenised while being un-re-themable. Exactly
the `--b-modal-width-xxl` bug from earlier the same day. Both added with their shipped fallbacks as
values, so no visual change.

**The two skins render different Office variants today.** Avalonia = `Large` (icon above a centred
label), web = `Medium` (16px icon, label to its right). Recorded in both enums' docs and folded into
TASK-099's context, because it means TASK-099 must *build* a missing rendering per skin rather than
re-parameterise existing ones — and must settle which variant is the ribbon's default look.

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

Landed 2026-07-29 on `task/TASK-098` across six repos.

**Model** — `Birko.Xaml.Core/Ribbon/RibbonModels.cs` gained `RibbonGroupSize`
(`Large`/`Medium`/`Small`/`Popup`, declared roomiest-first) and, on `RibbonGroup`, `Icon`,
`ScalingPriority` (default 0) and `MinSize` (default `Popup`). `b-ribbon.ts` gained the camelCased
mirror `RibbonGroupSize` + `icon` / `scalingPriority` / `minSize`, with the same prose in TSDoc.

**Tokens** — `tokens.json` + regenerate (`generate`, never hand-edited): `--b-ribbon-icon-large` 2rem,
`--b-ribbon-icon-small` 1rem, `--b-ribbon-chunk-width` 3.5rem, plus the two that were used-but-undefined
(`--b-ribbon-group-gap` 1.5rem, `--b-ribbon-item-gap` 0.25rem — their existing inline fallbacks, so no
visual change). Emits 5 CSS lines and 4 AXAML keys per theme, rem→px baked (32/16/56/24/4).

**Verification**
- `Birko.DesignTokens` `verify` — "all 5 CSS + 6 AXAML file(s) match tokens.json exactly".
- `Birko.DesignTokens.Tests` **42/42** (CSS + AXAML parity).
- `Birko.Xaml.Core.Tests` **46/46**, 5 new in `RibbonModelTests` (defaults, degrade ordering, floor,
  priority direction, group icon). `CoreIsAvaloniaFreeTests` still green, so Core stayed Avalonia-free.
- `Birko.Xaml.Avalonia.Tests` **153/153**, 1 new: a group carrying `Icon`+`ScalingPriority`+`MinSize`
  renders identically to one carrying none — the no-behaviour-change guarantee.
- `Birko.Web.Playground` verify **0 failures** across all five smokes.

**Not done here, by design:** nothing consumes the new fields. TASK-099 is still blocked on TASK-097
going `review → done`, which needs its manual resize pass.
