---
id: TASK-966
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: [TASK-965]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The shell's default user menu offers Profile in apps that have no profile page

## Context

Reported by DraCode (its TASK-056), 2026-11-22. `BCoreAppShell.getUserMenuItems()` always includes a Profile item that
goes to `getRoutes().profile` (`#/profile`), even when the app has no such route; DraCode overrides the menu to
Settings and Sign out. DraCode suggests making Profile opt-in.

**Decided** by the user (František Bereň, maintainer), 2026-11-22: opt-in (over hiding it when the profile route is
empty, and over leaving it as is). It changes the default menu without the consumer changing anything, so by README
§ Versions it is breaking: v0.5.0, shipped in its own push after v0.4.23 so the security fix (TASK-965) reaches
consumers as a patch.

## Change (Birko.Web v0.5.0)

- `BCoreAppShell` gains `protected get showProfileMenuItem(): boolean`, `false` by default. The default
  `getUserMenuItems()` puts Profile first only when it is `true`.
- CHANGELOG marks it BREAKING, with the migration. Symbio (a profile page, builds on `super.getUserMenuItems()`) was
  told and adds the getter when v0.5.0 lands; DraCode can drop its menu override.

## Acceptance criteria

- [x] Decided with the user: opt-in.
- [x] The default menu has no Profile item unless the app asks for it; an app that asks gets it first, as before.
- [x] CHANGELOG marks it BREAKING with the migration; Symbio told.
- [x] Playground check: red before, green after.

## Evidence

Birko.Web `task/TASK-966` 39a5128 (v0.5.0), Playground a74908d:
- backport-smoke, 1 TASK-966 check. Against main dcf19b2 it failed: the default menu was profile, settings, signout.
  Against the fix: settings, signout by default; profile, settings, signout when opted in. 482/482.
- verify 0 failing; contextual-ribbon 13/13; keyboard 244/244; entry-points 433/433; Shell `tsc` clean. All from
  builds that printed "✓ Built".

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-966` build: "all passed". Merged into Birko.Web main 1578c5e, tagged v0.5.0.

## Human test plan

1. In the Playground (Ctrl+Shift+R first), open Gallery → Navigation → the "Contextual tab (Shell)" card.
2. Open its user menu (the avatar at the top right of the card): it lists Settings and Sign out, no Profile.
