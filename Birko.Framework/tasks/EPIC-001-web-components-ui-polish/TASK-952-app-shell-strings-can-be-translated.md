---
id: TASK-952
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-21
depends-on: []
blocks: []
findings: [FIELD-053]
pr: null
github-issue: null
jira-key: null
---

# The app shell's own strings can be translated

## Context

Reported by the Symbio session, 2026-11-21, testing Symbio's admin in Czech and Slovak (its TASK-953): some strings
stayed English in every locale. Of the five it named, three are Birko.Web.Shell's:

- `b-app-shell.ts:307`: the command-palette button's `aria-label="Command palette (Ctrl+K)"`, hard-coded, with no hook;
- `b-core-app-shell.ts:113`: the theme switcher's label "Theme", from the `themeMenuLabel` getter (overridable, but
  English by default);
- `theme-registry.ts`: the built-in theme names ("Light", "Dark", …). An app could register its own labels, but only
  once, so they did not follow a locale switch.

"Settings" (Symbio's `shell/user-menu.ts`) and the "Notifications" label (Symbio's `notification-drawer.ts`) are
Symbio's own, handled in its TASK-1110.

## Decision

The user's, 2026-11-21: file and fix now, in the unpushed v0.4.20.

## Change (Birko.Web v0.4.20)

- The three strings read `bws.shell.commandPalette`, `bws.shell.theme` and `bws.shell.themes.<id>` from the global
  i18n, with the same English as before when a key is missing (the `bws.*` pattern of the shell's pages).
- `label-command-palette` and `label-theme` attributes on the shell element still win; `themeMenuLabel` stays
  overridable; a theme's name falls back to its registered label.
- The theme names follow a locale switch: `BaseComponent` re-renders on a locale change, and `onUpdated()` refreshes
  the theme menu.
- Docs: Shell CLAUDE.md and README key lists, CHANGELOG.

## Evidence

Playground `i18n-message-smoke`: the English half (3 checks) passes before and after; the 5 translation and attribute
checks fail on Birko.Web main and pass with the change (25/25 twice). verify 0 failing (backport 400/400);
contextual-ribbon 13/13; category-ribbon 19/19; locale-switch 47/47; locale-keys 23/23; keyboard 225/225;
rerender-leak PASS; a11y-name OK; entry-points 433/433; Shell `tsc` clean.

## Acceptance criteria

- [x] With no translation, the three strings are the English that shipped.
- [x] A consumer's locale bundle translates them through `bws.shell.*` keys, and the theme names follow a locale switch.
- [x] A per-instance attribute still beats a translation.

## Out of scope

- Symbio's own strings ("Settings", "Notifications"): Symbio TASK-1110.
- Shipping Czech or Slovak translations for `bws.*` with Birko.Web.

## Human test plan

1. In Symbio, with `bws.shell.commandPalette`, `bws.shell.theme` and `bws.shell.themes.*` added to its cs and sk
   bundles, switch the admin to Czech, then Slovak: the theme switcher's name and the theme names in its menu are
   translated, and a screen reader (or the accessibility tree) names the Ctrl+K button in that language.
2. Switch back to English: the same three read as before.

Merged into Birko.Web main (e9bd9ec), in v0.4.20, pushed 2026-11-21 at the user's word.

**Human test, 2026-11-21: PASS** — run by the Symbio session in Chrome on Symbio's admin (localhost:3000), Birko.Web
v0.4.20 with Symbio TASK-1110's keys, after clearing its service worker and caches:
Ctrl+K button "Paleta příkazů (Ctrl+K)" / "Paleta príkazov (Ctrl+K)" / "Command palette (Ctrl+K)"; theme switcher
"Motiv" / "Téma" / "Theme"; theme names "Světlý / Tmavý / Neon / Finstat", "Svetlá / Tmavá / Neon / Finstat",
"Light / Dark / Neon / Finstat" (cs / sk / en). Both steps passed.
