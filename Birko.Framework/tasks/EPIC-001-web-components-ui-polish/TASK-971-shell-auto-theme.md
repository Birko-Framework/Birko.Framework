---
id: TASK-971
parent: EPIC-001
feature: FEATURE-024
status: in-progress
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

- [ ] `setTheme('auto')` and a saved `auto` apply dark when the browser prefers dark and dark is registered, else light; a change of the browser setting applies at once; `currentTheme` reports `auto`.
- [ ] An app's `defaultTheme` (default `light`) is the starting theme when nothing is saved.
- [ ] The theme menu lists Auto first, ticked when chosen, only when dark is registered; its label is `bws.shell.themes.auto`, else "Auto".
- [ ] The media listener is removed when the shell goes.
- [ ] Playground check (the browser setting emulated).

## Human test plan

1. In the Playground's Contextual tab (Shell) card, open its theme menu: Auto is first.
2. Pick Auto: the app follows your system's light/dark setting. Change the system setting (or DevTools → Rendering → prefers-color-scheme): it switches at once.
3. Reload: still Auto.
