---
id: TASK-971
parent: EPIC-001
feature: FEATURE-024
status: verify
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# App shell: an Auto theme that follows the browser's light or dark setting

## Context

FEATURE-024 D1-D4 (DraCode, its TASK-188). Auto applies dark or light from the browser's `prefers-color-scheme` and follows it live; the shell remembers Auto, not the theme it showed. Auto is a starting theme only for apps that ask (D2), chooses between light and dark and is offered only when dark is registered (D3), and is the first item of the theme menu, translatable (D4). Built on `task/TASK-968` for v0.5.2.

## Acceptance criteria

- [x] `setTheme('auto')` and a saved `auto` apply dark when the browser prefers dark and dark is registered, else light; a change of the browser setting applies at once; `currentTheme` reports `auto`.
- [x] An app's `defaultTheme` (default `light`) is the starting theme when nothing is saved.
- [x] The theme menu lists Auto first, ticked when chosen, only when dark is registered; its label is `bws.shell.themes.auto`, else "Auto".
- [x] The media listener is removed when the shell goes.
- [x] Playground check (the browser setting emulated).

## Evidence

Birko.Web `task/TASK-968` 3bcc1e1 (v0.5.2), Playground ef425d8. verify 0 failing (backport 492/492; carousel-smoke failed once, passed on the rerun); keyboard 244/244; a11y-name OK; contextual-ribbon 13/13; locale-keys 23/23; locale-switch 47/47; rerender-leak PASS; entry-points 433/433; device-fix 247/247; sortable 19/19; tree drag 17/17; escape-in-modal 27/27; theme-roundtrip 11/11; Components and Shell `tsc` clean. All from builds that printed "✓ Built".
- backport-smoke, with the browser setting emulated: an app starting on Auto applies dark, switches to light and back as the setting changes, lists Auto first and ticked; a chosen light stays light; Auto is saved as `auto`; the listener is removed with the shell; without dark, Auto is light and not offered; an app that does not opt in starts light. i18n-message: the theme names now start with Auto (`bws.shell.themes.auto` translated). Chrome: the Contextual tab (Shell) menu lists Auto, Light, Dark; Auto with a light system shows light.

## Human test plan

1. In the Playground's Contextual tab (Shell) card, open its theme menu: Auto is first.
2. Pick Auto: the app follows your system's light/dark setting. Change the system setting (or DevTools → Rendering → prefers-color-scheme): it switches at once.
3. Reload: still Auto.
