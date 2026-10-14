---
id: TASK-592
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: in-progress
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-rating` draws only a star: let a consumer supply the icon through a slot

## Context

Asked for by the user on 2026-10-14, after signing off TASK-579. `b-rating`'s shape is the `STAR` SVG constant in
`Birko.Web.Components/src/inputs/b-rating.ts`, rendered once per star. A consumer can change its colour
(`--b-rating-color`) and size (`size`, `--b-rating-star`), but not its shape: hearts, paws or a brand mark need a
framework change today.

**Decided shape (user, 2026-10-14): an `icon` slot.** The consumer puts one SVG in the light DOM:

```html
<b-rating name="love" label="How much?">
  <svg slot="icon" viewBox="0 0 24 24"><path d="…heart…"/></svg>
</b-rating>
```

The component copies it into each star. The alternatives were an `icon="heart"` attribute choosing from a built-in set
(simplest, but limited to the shapes shipped) and an app-wide `BRating.setIcon()` (one shape for the whole app); the
slot was chosen for freedom.

What makes it more than a slot:
- **One icon, many stars.** A slotted node renders once, so the component must clone the slotted SVG into each star
  (in its shadow root) and re-clone when the slot's content changes (`slotchange`). The original stays in the light DOM,
  hidden.
- **Colouring has to keep working.** Filled and empty stars are painted by the component's `fill` and `stroke` rules.
  A slotted SVG that hard-codes its own `fill` or `stroke` (most icon exports do) would ignore them, so filled and empty
  would look the same: the state would be invisible. The component must strip or override presentation attributes on
  the clone, or document a contract (`fill="currentColor"`-style) and enforce it.
- **Contrast is a guarantee today** (TASK-579: 3:1 in every theme on two backgrounds). It must still hold for a slotted
  icon, since the colours stay the component's.
- **Accessibility unchanged:** the clones are decorative (`aria-hidden="true"`, `focusable="false"`); names still come
  from `bwc.rating.*`. The slotted original must not be announced.
- **Read-only** uses the same icon.

## Acceptance criteria

- [ ] An `icon` slot: a slotted SVG replaces the star in every position, in the operable and read-only modes; with
      nothing slotted, the built-in star renders exactly as today
- [ ] The clone follows the component's colours whatever the slotted SVG declares: filled and empty are visibly
      different, and the contrast stays 3:1 or more in every shipped theme on `--b-bg` and `--b-bg-secondary` (the
      existing rating-smoke contrast checks run against a slotted icon too)
- [ ] Changing or removing the slotted icon at runtime re-renders the stars (`slotchange`); no duplicate or stale
      clones, and keyboard focus and the value survive it
- [ ] The slotted original is not visible and not announced; the clones are `aria-hidden` and not focusable; the
      accessible names and keyboard behaviour are unchanged (keyboard-check still passes)
- [ ] A non-SVG or empty slot is ignored (the star is used), not rendered as text
- [ ] README (b-rating section) shows the slot and its colouring contract; CLAUDE.md inventory row lists the slot;
      the Playground Rating card offers a heart variant; rating-smoke covers the slot; a PATCH release note

## Out of scope

- Half or partial icons.
- A built-in icon set (`icon="heart"`) and an app-wide `setIcon()`: not chosen; file separately if wanted later.
- Multi-colour icons: the component paints the icon in its own fill and stroke, so a two-tone icon becomes one colour.

## Human test plan

- [ ] On the published Playground, the Rating card's heart variant: filled and empty hearts are clearly different in
      light, dark and finstat; keyboard and a screen reader behave as with the star (TASK-579's checks)

## Implementation plan

Drafted 2026-10-14 at pick. Code in `Birko.Web.Components/src/inputs/b-rating.ts`; tests in the Playground.

1. **Source of the icon:** `render()` reads the host's first light-DOM child `svg[slot="icon"]`. A
   `<slot name="icon">` in the shadow root is `display: none`, so the original is neither shown nor in the
   accessibility tree, and its `slotchange` triggers `update()` (a replaced or removed icon re-renders; the morph keeps
   the radios, so the value and focus survive). Nothing slotted, or no SVG among it, means the built-in star.
2. **The copy:** the slotted SVG is cloned once per render and serialised into each star: `aria-hidden="true"`,
   `focusable="false"`, no `style` attribute anywhere in it, no `on*` attributes, no `<script>` or `<foreignObject>`
   (consumer markup re-parsed into the shadow root must not run anything).
3. **Colouring:** `.star svg * { fill: inherit; stroke: inherit; stroke-width: inherit }`. Author CSS outranks SVG
   presentation attributes, so a path's own `fill="#e11d48"` yields to the component's filled/empty paint. Only an inline
   `style` would outrank it, hence step 2. Measure in the browser, don't assume.
4. **Tests:**
   - `rating-smoke`: slotted icon in every star, operable and read-only; built-in star when empty or non-SVG; an icon
     with hard-coded `fill`/`stroke`/`style` still paints filled and empty differently; the contrast loop repeated with
     the slotted icon; a runtime swap and a removal re-render without duplicate clones and keep the value; the original
     not rendered; clones `aria-hidden`; `on*` stripped.
   - `keyboard-check`: tab and arrows with a slotted icon, and focus surviving an icon swap.
   - Prove red against the old component.
5. **Docs and release:** README section (slot + colouring contract), CLAUDE.md inventory row, a heart variant on the
   Playground Rating card, CHANGELOG at release (v0.1.5).
