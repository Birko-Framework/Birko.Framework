---
id: TASK-972
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

# App shell: a snippet that applies the saved theme before the first paint

## Context

FEATURE-024 D5: before the app's code loads, the page shows the light look, so a dark browser on Auto (or a saved dark) flashes light first. The framework documents a short script an app puts in its page head that applies the saved theme, Auto included, before the first paint. Built on `task/TASK-968` for v0.5.2.

## Acceptance criteria

- [x] The Shell README gives the snippet, with the app's storage prefix and starting theme to fill in.
- [x] The snippet applies what the shell would apply (light, a saved theme, Auto resolved) and fails silently where storage is blocked.
- [x] Playground check of the snippet's logic.

## Evidence

Birko.Web `task/TASK-968` 3bcc1e1 (v0.5.2), Playground ef425d8. verify 0 failing (backport 492/492; carousel-smoke failed once, passed on the rerun); keyboard 244/244; a11y-name OK; contextual-ribbon 13/13; locale-keys 23/23; locale-switch 47/47; rerender-leak PASS; entry-points 433/433; device-fix 247/247; sortable 19/19; tree drag 17/17; escape-in-modal 27/27; theme-roundtrip 11/11; Components and Shell `tsc` clean. All from builds that printed "✓ Built".
- backport-smoke: the script gives dark for a saved auto on a dark browser, light without dark, dark for an auto starting theme, light by default, a saved neon as neon, light for auto on a light browser. The README snippet is checked to be exactly `themeBootScript({ prefix: 'app', defaultTheme: 'auto' })`.

## Human test plan

1. In an app with the snippet and Auto, with the system set to dark: reload. The page is dark from the first frame, with no light flash.
