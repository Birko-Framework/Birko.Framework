---
id: TASK-813
parent: EPIC-001
feature: null
status: done  # signed off by the user 2026-11-04
priority: P2
assignee: ai
created: 2026-11-04
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon's key tips show under the menu or flyout they belong to

Found by the user in Symbio (screenshot 2026-11-04): Alt+Windows, a category tab, then a module's split ▾ (Hotel) —
the menu opens, and its choices' key tips sit *behind* it, only a dark sliver of each showing at the menu's right edge.

## Context

The badges live in one manual popover (`.ribbon-keytips`, TASK-736), which its comment says "shows over a flyout or a
menu". The top layer stacks in show order, though: `_showKeytips` only calls `showPopover()` when the layer is closed,
and while key tips are up it is already open, so a menu or flyout opened by a key tip (or a gallery grid) is shown
after it and stacks above it. TASK-736's checks counted the badges' text, never what is painted on top.

Measured (keyboard-check, real keys, on Birko.Web main): the menu covered both of its badges, the folded group's
flyout all 4, the gallery grid all 8.

## Acceptance criteria

- [x] Reproduced first with real keys in the Playground: at a menu choice's badge, `elementFromPoint` hits the menu.
- [x] A menu, a folded group's flyout and a gallery grid opened by a key tip: every badge is on top of it.
- [x] keyboard-check covers it and fails without the fix.

## Out of scope

- Key tip letters and placement (unchanged).

## Human test plan

- [x] Symbio, Alt+Windows, a category tab's letter, then the letter of a module's ▾ (Hotel): each menu choice shows a
      whole dark badge with its letter, on top of the menu; pressing one opens that page. **PASS (the user, 2026-11-04)**, on a Symbio bundle built against the
      branch (the dev server had overwritten the first one from main within a minute).

## Built (2026-11-04), on branches, not merged

`_showKeytips` hides and re-shows the badge layer for every level, so it is always the last thing in the top layer.

- Birko.Web `task/TASK-813` ca3f53f (worktree `C:\Source\Birko\Web-TASK-813`; version bump at merge).
- Playground `task/TASK-813` 849262c: three paint-order checks (`tipsOnTop`: `elementFromPoint` at each badge's
  centre, with the layer's `pointer-events: none` lifted for the measurement). keyboard-check 225/225, three runs;
  222/225 on Birko.Web main (the three new checks). category-ribbon-check 19/19, verify.mjs clean.
- Not run: `tsc` (the Playground's tsconfig paths point at the main checkout, not the worktree); esbuild bundled it.

## Merged (2026-11-04)

Birko.Web main 32b582d, tagged **v0.4.5** locally; Playground main d1b1822. Not pushed. Worktrees and branches
removed.
