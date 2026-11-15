---
id: TASK-930
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-15
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The mobile shell's top bar counts the top safe-area inset twice

Found on the user's iPhone (iOS 27.2) on 2026-11-15, while re-measuring TASK-600 with Reps served through a
Cloudflare quick tunnel. Fixed at the user's word ("go ahead with all three").

## Context

`BMobileAppShell`'s `.mobile-topbar` sets `height: calc(header + env(safe-area-inset-top))` and
`padding-top: env(safe-area-inset-top)`, and its own comment says box-sizing:border-box keeps the usable bar at
header-height. Nothing declared it: `reset.css`'s `* { box-sizing: border-box }` is a document rule and does not reach
the shell's shadow DOM, so the bar was `content-box` and drew the inset twice. With `viewport-fit=cover` +
`black-translucent` the iPhone measured `top-bar 0..143` (42 + 50 + 50): an empty band under the status bar. It is the
"over-padded the top bar and left a visible gap" Reps' index.html records as the reason it left `black-translucent`.
Reproduced in Chrome with a substituted 50px inset (143), and the device showed `0..92` with border-box patched in
(variant C).

## Acceptance criteria

- [x] `.mobile-topbar` is `border-box`; with an inset it grows by the inset once.
- [x] Regression check, red before.
- [x] CHANGELOG.

## Human test plan

- [x] Already run on the device before the fix landed: the iPhone (iOS 27.2) showed `top-bar 0..92` with border-box
      patched into the built bundle (TASK-600, variant C). Apps without `viewport-fit=cover` (inset 0, Reps as
      shipped) do not change.

## Done (2026-11-15)

`box-sizing: border-box` on `.mobile-topbar`. Playground `backport-smoke`: 2 FAIL against unfixed code (content-box;
+100px for a 50px inset), 326/326 fixed; verify.mjs 0 failing; Shell `tsc` clean. Birko.Web main, in the unpushed
**v0.4.13** (tag moved); Playground main. Not pushed.
