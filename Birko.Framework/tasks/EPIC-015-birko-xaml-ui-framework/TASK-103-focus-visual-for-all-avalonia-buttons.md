---
id: TASK-103
parent: EPIC-015
feature: null
status: todo  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Every Avalonia control needs a focus visual — `Buttons.axaml` has none

## Context

Found during TASK-100's keyboard pass, and it is the root cause of two separate "Tab is broken" reports
that were not Tab being broken at all.

`Birko.Xaml.Avalonia/Themes/Buttons.axaml` styles no `:focus` state. Neither do most of the other
control theme files — only `Inputs.axaml` has one. So **keyboard focus is invisible on every button in
every consumer app.** The web side has had `--b-focus-ring` throughout; the token exists in
`tokens.json` and in the generated CSS, but is **never emitted to AXAML**, which is how the gap survived
the design-token parity gates.

**Why this is a defect and not polish.** WCAG 2.4.7 (Focus Visible) is a Level AA criterion. An app built
on Birko.Xaml is currently unusable by keyboard-only users not because navigation is broken but because
there is no way to tell where you are. Both reports it produced looked like navigation bugs:

1. "Tab only swipes between the 4 theme buttons, it never lands on the ribbon" — it did land on the
   ribbon, invisibly, all along. TASK-100 added a ribbon-scoped focus ring and the report resolved.
2. "Tab on the last command returns to the theme buttons and stays on them" — the same illusion one level
   out. Focus leaves the ribbon and moves through the gallery's tab headers and content, all of which take
   focus invisibly; the theme buttons are the only stop where anything is visible, so focus appears to
   return there and stop.

The second report is *still open* precisely because this task is not done. It is not a gallery bug, and
patching the gallery would hide it rather than fix it.

**Scope note on why this is separate from TASK-100.** TASK-100 gave the ribbon's own buttons a focus ring
because the ribbon could not be signed off without one. That was deliberately scoped: a focus visual on
`Buttons.axaml` changes the appearance of **every button in every consumer**, which is a framework-wide
visual change that deserves its own review rather than arriving as a side effect of a ribbon story.

## Acceptance criteria

- [ ] `--b-focus-ring` (and any related focus tokens) are **emitted to AXAML** by `Birko.DesignTokens`,
      not just to CSS. Guarded by `AxamlParityTests` like the rest of the generated dictionaries.
- [ ] `Buttons.axaml` gives every button variant a visible `:focus-visible` indicator using that token.
- [ ] Sweep the remaining control theme files for the same gap and fix each: anything focusable must show
      it. Enumerate what was checked, so "we looked at buttons only" is not the outcome.
- [ ] The indicator does **not change layout** — constant border thickness or an outside-drawn adorner. A
      focus ring that resizes its control reflows its row, which for the ribbon would break the scaling in
      STORY-049. TASK-100's ribbon ring keeps thickness constant and changes only the brush; follow that.
- [ ] Prefer `:focus-visible` over `:focus` so a mouse click does not leave a ring behind, matching the web.
- [ ] Contrast: the ring must be visible against every theme's button backgrounds — all four themes,
      including the primary/filled variants where the ring sits on a saturated fill rather than the page
      background. Check contrast rather than eyeballing one theme.
- [ ] The ribbon's task-scoped ring in `Ribbon.cs` (`AddFocusRing`, plus its `FocusRingFallback`) is
      **removed** in favour of the theme-level one, or explicitly kept with a recorded reason. Two
      mechanisms for one thing is how they drift.
- [ ] Coverage in `Birko.Xaml.Avalonia.Tests`, and it must assert the **outcome** — a focused control looks
      different from an unfocused one, and its bounds are unchanged. Mutation-check it: TASK-100's whole
      lesson was that a test asserting the mechanism passes while the user cannot use the app.

## Out of scope

- **KeyTips / Alt accelerators** — still a separate shell-wide keyboard story (noted in TASK-100).
- Changing tab-order behaviour anywhere. Traversal is correct; only its visibility is missing.
- The web side, which already has this.
- Focus visuals for the Shell's chrome beyond what the token change covers, if that turns out to need its
  own design pass — split it out rather than growing this task.

## Human test plan

- [ ] `Birko.Xaml.Gallery` — from the address of the first control, Tab all the way through the window and
      confirm **every** stop is visible: theme buttons, tab headers, controls inside a tab, the ribbon.
      This is the report that is still open; it should resolve without any gallery change.
- [ ] Repeat in each of the four themes, watching for a ring that vanishes on a filled/primary button.
- [ ] Click a button with the mouse. Expected: no lingering ring (the `:focus-visible` criterion).
- [ ] Confirm nothing shifts position when focus arrives — particularly a ribbon row at a width where
      groups are scaled, where a reflow would be visible as the row re-deciding its variants.

## Implementation plan

_Populated by `/tasks plan TASK-103` — leave empty until then._
