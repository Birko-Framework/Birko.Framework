---
id: TASK-965
parent: EPIC-001
feature: null
status: done
priority: P0
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: [TASK-966, TASK-524]
findings: []
pr: null
github-issue: null
jira-key: null
---

# SECURITY: the shell's user menu puts the user's name into the page unescaped

## Context

Reported by DraCode (its TASK-056, signing in with GitHub), 2026-11-22. `BCoreAppShell.renderUserDropdown` put
`getUserName()` and `getUserInitials()` into its HTML as they came. The name usually comes from the token's `name`
claim (`createAuthStore` maps it to `userName`); for a GitHub or other federated sign-in that is a profile field anyone
can set, so a name like `<img src=x onerror=…>` ran script in the header of every Birko app that uses the default user
menu. Confirmed on Birko.Web main 2d02508 (v0.4.22). Symbio's shell returns the auth store's `userName` unchanged.

DraCode works around it by escaping in its own `getUserName` / `getUserInitials` overrides. Once this ships, those
overrides must return plain text again, or a name with `&`, `<` or a quote shows escaped twice.

## Change (Birko.Web v0.4.23)

- `renderUserDropdown` escapes the name and the initials where it renders them. `getUserName()` and `getUserInitials()`
  return plain text; their doc comments say so.

## Acceptance criteria

- [x] A user name holding markup renders as text in the header: no element and no handler from it, in the name or in
      the avatar initials.
- [x] An ordinary name renders as before.
- [x] Playground check: red before, green after.
- [x] CHANGELOG notes that overrides must return plain text (DraCode's escaping overrides would now double-escape).

## Out of scope

- The Profile item in the default user menu: [[TASK-966]].

## Evidence

Birko.Web `task/TASK-965` 27a259e (v0.4.23), Playground 7b59e5c:
- backport-smoke, 2 TASK-965 checks. Against main 2d02508 the hostile-name check failed: the name read `"Eve" & co`
  (the `<img>` had become an element), 1 injected element, and its `onerror` handler ran. Against the fix: 481/481,
  the name and avatar `<I` render as text, nothing injected, no handler ran; an ordinary name renders as before.
- Chrome, the gallery's Contextual tab (Shell) card, whose user name is now deliberately hostile: the header reads the
  markup as text, avatar `<I`, no `<img>` in it, no "TASK-965" console error.
- verify: 0 failing; contextual-ribbon 13/13; keyboard 244/244; a11y-name OK; entry-points 433/433; Shell `tsc`
  clean. All from builds that printed "✓ Built".

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-965` build: "test was as you told". Merged into Birko.Web main dcf19b2, tagged v0.4.23.

## Human test plan

1. In the Playground (Ctrl+Shift+R first), open Gallery → Navigation → the "Contextual tab (Shell)" card.
2. Its header shows the user name `<img src=x onerror=…> You` as literal text beside an avatar reading `<I`: no broken
   image icon, and the browser console shows no "TASK-965: the user name ran script" error.
3. The demo still works: select an order, its Order tab appears.
