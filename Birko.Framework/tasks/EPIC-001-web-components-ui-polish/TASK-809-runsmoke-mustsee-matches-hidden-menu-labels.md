---
id: TASK-809
parent: EPIC-001
feature: null
status: done
priority: P1
assignee: ai
created: 2026-11-03
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# birko-web-testing: runSmoke's mustSee and expectSelector take the first VISIBLE match

Found by Symbio (its browser gate: 522 tests, 1 failed, blocking its TASK-1027).

## Context

`Birko.Web.Testing/src/playwright/smoke.ts` asserted `page.getByText(text, { exact: false }).first()` visible for each
`mustSee`, and `page.locator(expectSelector).first()`. Since v0.4.1 (TASK-803) the category ribbon's ▾ menus keep
every page label in the DOM, hidden until opened, so for a text that is also a page name the first match is a
hidden `.ribbon-menu-label`: Symbio's `smoke.spec.ts › Communication dashboard`, `mustSee: ['Inquiries']` →
"13 × locator resolved to <span class="ribbon-menu-label">Inquiries</span> — unexpected value hidden". Every
consumer whose `mustSee` names a page hits it. `manifest-smoke.ts`'s `expectSelector` had the same `.first()`.

## Acceptance criteria

- [x] `mustSee` and `expectSelector` (runSmoke) and `expectSelector` (runManifestSmoke) pass on the first visible
      match, with `.locator('visible=true')`, which the declared Playwright floor (>=1.48) already has; a text or
      selector that only matches hidden elements still fails.
- [x] A check that fails without it: Playground `testing-smoke-check.mjs` drives the real `runSmoke` with a real
      Playwright against a page whose hidden menu holds the heading's text first (2/4 before, 4/4 after).
- [x] Symbio told when it is on Birko.Web main.
- [x] Symbio's gate re-run green (its smoke.spec.ts › Communication dashboard): smoke and ribbon specs 19/19 on
      v0.4.2 (Symbio, 2026-11-03).

## Out of scope

- `runManifestSmoke` has no check of its own here (it needs a module-manifest endpoint and a login); its change is
  the same one-line locator.

## Human test plan

N/A — fully covered by automated tests (`testing-smoke-check.mjs`), and Symbio's gate re-run is the consumer proof.

## Built (2026-11-03), on branches with TASK-808, not merged

Birko.Web `task/TASK-808` c5a157a (v0.4.2), Playground `task/TASK-808` 10ddd9d (`testing-smoke-check.mjs`, Playwright 1.61.1 from the
Reps ui-e2e suite).

## Merged (2026-11-03)

Birko.Web main 3260449, tagged **v0.4.2** and pushed; Playground main 6d27396, pushed. Symbio told before and after.
Worktrees and branches removed.
