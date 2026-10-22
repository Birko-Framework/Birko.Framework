---
id: TASK-736
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-22
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-731, TASK-732]
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: key tips (Alt shows letters for keyboard access)

## Context

FEATURE-019 D7 (approved by the user 2026-10-22): Pressing Alt shows a small letter on every tab; pressing it opens that tab and shows letters on its commands, so anything in the ribbon can be reached in two or three key presses.

No key tips exist. Alt is also the browser menu key on some platforms, so the trigger needs care.

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] Alt+Windows key (Ctrl+Option on a Mac) shows tab tips, as in Microsoft 365 in the browser (decided by the user,
      Q2); an attribute changes the combination; a letter opens the tab and shows command tips; Escape goes back one
      level
- [x] Tips are declared or derived (first free letter) without collisions, per locale
- [x] Menus and galleries get their own level
- [x] Does not break typing in inputs, nor the browser's own Alt behaviour
- [x] keyboard-check.mjs covers it with real key presses
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [x] Reach three different commands with key tips only; type Alt inside an input and check nothing fires

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed.

- **Decided by the user.** Alt+Windows key / Ctrl+Option (Q2), Office's dark squares (from a three-option preview),
  and automatic letters with app overrides.
- **API.**
  - `keytip?: string` on `RibbonTab`, `RibbonGroup` (its folded button), `RibbonItem` and `RibbonMenuItem`.
  - Attribute `keytip-shortcut`: modifier-only combos such as `Alt+Meta`, or combos with a key such as
    `Ctrl+Shift+K`; `none` turns it off. Default `Alt+Meta`, on a Mac `Ctrl+Alt`.
- **Levels.**
  - Tabs, plus digits 1–9 on the toolbar's commands (not its ▾). Selecting a tab shows its commands.
  - A folded group opens its flyout, and a split ▾ or menu-only command opens its menu, each with its own letters.
  - A plain command or toggle runs (`click()`) and ends the mode. A panelless tab or tabs-only mode just clicks the
    tab.
  - Escape goes back one level. A lone modifier keeps the mode. An unknown letter ends it and is swallowed, never
    typed. `pointerdown`, `resize` and the window blurring end it.
- **Letters.** Declared first, then the first free letter or digit of the label (NFD-folded), then any free one. 36 a
  level; extra items get none, with no two-letter tips.
- **Look.** Dark squares in the dark tooltip tokens. Tabs and toolbar buttons carry them centred underneath, commands
  on their bottom-right corner. They sit in a manual top-layer popover, so they show over flyouts and menus. Checked in
  Chrome on the gallery ribbon, both levels.
- **Bug found and fixed in this work.** A captured `blur` listener on `window` also saw element blurs, so focus moving
  into an opened menu ended the mode. Now it is non-capturing, with `target === window`.
- **Gate.**
  - Playground `keyboard-check.mjs` 137/137, three runs, with 18 new checks using real keys. They cover: typing in an
    input unaffected; tab letters and toolbar digits; a declared tab tip; Escape levels; command letters (declared V,
    first-free C/O, accent-folded Č → I); running a command; the ▾ menu level and a choice with parentId; a toolbar
    digit; a lone modifier; an unknown letter swallowed; a click ending the mode; a folded flyout level and a command
    in it; `none`; rebinding.
  - The check crashes at its first key-tip step on the TASK-737 ribbon. `verify.mjs` clean; entry-points 409/409;
    `tsc` clean.
- **Test notes.** At 110–150px a two-group test ribbon either fit or squeezed the tab strip to 0px (toolbar plus
  controls), so the folded case uses four groups at 190px.

## Sign-off (2026-10-22)

Signed off by the user in the Playground: spacing, the look in every theme, hover, key tips with Alt+Win and the
overall feel are good. The screen-reader checks were done through Chromium's accessibility tree instead of a live
screen reader, as the user asked (`ribbon-a11y-check.mjs`, 22/22; it found and fixed a menu shortcut announced
twice). Behaviour is covered by the automated suites: keyboard-check 195/195, `verify.mjs` clean.
