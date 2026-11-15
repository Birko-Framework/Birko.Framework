---
id: TASK-921
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-12
depends-on: [TASK-804]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A page can re-raise its contextual ribbon tab for the same record AND open it

Asked for by Symbio (TASK-1051, the user's ruling 2026-11-12), through the Symbio session.

## Context

TASK-804's `ContextualRibbonLink.raise` opens the tab only for a NEW record key
(`select = key !== _raisedFor && (ribbon.open ?? true)`), so `refreshContextualRibbon()` for the same record never
opens it. That is right for a status refresh after a command.

Symbio pages now add a second ribbon group for a sub-row selected inside the detail (an event's attendee, a
production step). On an UNPINNED ribbon the pointer leaves the panel to click that row, and the panel collapses
(the 300 ms hover-leave timer in `b-ribbon`; measured: panel 1 px, the contextual tab still active). The re-raise
cannot open it, so the user does not see the new group until they click the tab. The user ruled: selecting a
sub-row SHOULD open the panel.

## Acceptance criteria

- [x] `refreshContextualRibbon({ open: true })` on `BaseSplitPage` and `BaseDetailPage` re-raises with select, through
      `ContextualRibbonLink.raise(record, key, { select })` (an explicit `select` overrides the new-key rule).
- [x] The default (`refreshContextualRibbon()`, a status refresh) still does not open it.
- [x] On a pinned ribbon, `{ open: true }` for the tab already open is harmless: no collapse, no tab change.
- [x] `b-ribbon.showContextual(id, { select: true })` cancels a collapse the pointer leaving the panel has already
      queued, because the click that selects the sub-row is usually that one.
- [x] Test: unpinned ribbon, tab raised then collapsed by the pointer leaving → `refreshContextualRibbon({ open: true })`
      → panel expanded with the tab active. Shell README, CHANGELOG, version bump.

## Out of scope

- Which Symbio pages use it: Symbio TASK-1051 (events, manufacturing).

## Human test plan

- [x] Playground "Contextual tab (Shell)" card or Symbio's events page, ribbon UNPINNED (Ctrl+F1): select a record, so
      its tab opens; move the pointer out of the panel so it closes; then trigger the sub-row selection (Symbio: click an
      attendee row). Expected: the panel opens again on the contextual tab, with no flicker of another tab. Then pin the
      ribbon and repeat: nothing visibly changes besides the new group.
      **PASS (the user, 2026-11-12, in Symbio on TASK-1051's build: the sub-row behaviour confirmed, opening on an
      unpinned ribbon included; relayed by the Symbio session).** The pinned case was not reported separately; it is
      covered by the smoke's MutationObserver check.

## Implementation plan

- `Birko.Web.Shell/src/pages/contextual-ribbon.ts`: `raise(record, key, options: { select?: boolean } = {})`,
  `select = options.select ?? (new key && open)`.
- `base-split-page.ts` / `base-detail-page.ts`: `refreshContextualRibbon(options: { open?: boolean } = {})` passes
  `{ select: options.open }`.
- `b-ribbon.showContextual`: `_clearTimers()` before selecting.
- Playground: `contextual-ribbon-smoke` (split and detail pages: unpinned default vs open, pinned no-flicker by a
  MutationObserver), `contextual-ribbon-check.mjs` (real mouse: unpinned, leave the panel, re-raise after and inside
  the collapse delay). v0.4.12.

## Built (2026-11-12), on branches, not merged

Birko.Web `task/TASK-921` 9ff1ee7 (worktree `Web.TASK-921`, v0.4.12); Playground `task/TASK-921` f1fc97c (worktree
`Birko.Web.Playground.TASK-921`). Reproduced Symbio's measurement on the Playground card: unpinned, the pointer leaving
the panel collapses it to 1 px with the contextual tab still active. `contextual-ribbon-smoke` 28/28 (26/28 against
Web main: both `{ open: true }` cases fail), `contextual-ribbon-check.mjs` 13/13 with a real mouse (11/13 against main;
12/13 with the `_clearTimers()` in `showContextual` removed, the re-raise inside the 300 ms collapse delay is undone).
Shell and Components `tsc` clean. verify.mjs: 2 failing, both `picker-popup-smoke` b-time Escape, failing identically
against unmodified Web main (pre-existing, not this change).

## Merged (2026-11-12), not pushed

At the user's word: Birko.Web main 156297e, tagged **v0.4.12** locally; Playground main c44c7fd. Not pushed (batch
with the next push, with the tag). Symbio told; its dev server watches the local checkout, so it builds on v0.4.12.
Worktrees and branches removed. Status `verify` until the human test plan runs (in Symbio, TASK-1051).

## Closed (2026-11-12)

Human test passed in Symbio (above). Symbio adopted it in TASK-1051 (1b4afb17: events attendee and venue-booking
rows, manufacturing production steps). Birko.Web v0.4.12 and the Playground merge are still local; push with the
next batch, with the tag.

Release consolidated (2026-11-15, the user's call): the unpushed Birko.Web tags v0.4.12-v0.4.17 were folded into one
**v0.4.12** (Birko.Web 635340e), which carries this change. Any v0.4.13-v0.4.17 above refers to a tag that no longer exists.
