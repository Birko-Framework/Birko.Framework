---
id: TASK-923
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-14
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-tree-menu: `TreeMenuItem.muted` shows a row for context, still selectable

Asked for by Symbio (TASK-1089), through the Symbio session, 2026-11-14.

## Context

The Symbio pages screen is getting a category filter that keeps the matching pages and their ancestors. An ancestor
outside the category is shown muted, so a match keeps its place in the tree. `disabled` already dims a row but also
blocks selection, and the user decided muted ancestors stay clickable.

Symbio already passes `muted` on its tree items (`pages-page.ts`); its ui-e2e spec asserts `toHaveClass(/muted/)` on
`#page-tree .node-row`, so the class name `muted` is the contract.

## Acceptance criteria

- [x] `TreeMenuItem.muted?: boolean`; the row's `.node-row` gets the `muted` class.
- [x] Text in `--b-text-muted`; no `aria-disabled`, no opacity / `pointer-events` change.
- [x] Selectable by click and by Enter / Space; `select` fires as usual; arrow keys move through it.
- [x] Children and expansion behave as for any row.
- [x] Components README + API.md document the flag; CHANGELOG, v0.4.13.

Note: "slightly dimmed" is delivered by the muted colour alone, not an extra opacity — `--b-text-muted` (#607086) is
already the dimmest text token, and opacity on top would drop it below AA contrast on a hover/plain background.

## Human test plan

- [x] In Symbio's pages screen (TASK-1089), pick a category: ancestors outside it read muted (grey text, not faded
      like a disabled row); click one — it is selected and its page opens; Tab / arrow to one and press Enter — same.
      Hover a muted row: it highlights like any row.
      **PASS (the user, 2026-11-14, in Symbio on TASK-1089: non-matching ancestors muted, click and Enter select;
      relayed by the Symbio session).** Hover was not reported separately.

## Built (2026-11-14)

Birko.Web `task/TASK-923` (worktree `Web.TASK-923`); Playground `task/TASK-923` (worktree
`Birko.Web.Playground.TASK-923`): `tree-sort-smoke` section 10 (class, computed colour equals `--b-text-muted`, no
`aria-disabled`, focusable, children shown, click and Enter select while a disabled row does not, ↓ moves through it).
tree-sort-smoke 34/34; verify.mjs failing checks 0. Components `tsc` (TypeScript 5.6.3) clean.

## Merged (2026-11-14), not pushed

Birko.Web main 6a20347, tagged **v0.4.13** locally; Playground main e41e9df. Not pushed (batch with the next push,
with the tag). Symbio told; its dev server watches the local checkout. Worktrees and branches removed. Status
`verify` until the human test plan runs in Symbio (TASK-1089).

Symbio session, 2026-11-14: `muted` works in Symbio (ancestors muted; click and Enter select). The user's human test
of TASK-1089 is still pending.

## Closed (2026-11-14)

Human test passed in Symbio (above); Symbio gate 3 PASS on that build (564 tests, only known failures). Birko.Web
v0.4.13 and the Playground merge are still local; push with the next batch, with the tag.
