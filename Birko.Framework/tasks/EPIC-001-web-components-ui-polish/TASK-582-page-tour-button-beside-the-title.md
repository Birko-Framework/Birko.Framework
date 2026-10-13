---
id: TASK-582
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: done
assignee: ai
created: 2026-10-13
depends-on: [TASK-584]
blocks: []
pr: null  # Birko.Web eec672b, shipped in v0.1.3; Playground aa8aa24 + 6d3e63c
github-issue: null
jira-key: null
---

# `BasePage` has no home for a page's guided tour, so consumers put the `?` in the action group

## Context

Filed from Symbio TASK-677, decided 2026-10-13. Symbio launches a page's guided tour from a `?` button that each
page appends to `renderHeaderActions()` (`Symbio.UI/src/shared/tour-button.ts`). On a read-only page
(`formSchema = null`, so no Create button) the action group holds nothing else, and the `?` sits there alone.
Symbio measured 8 pages with a tour, and on 5 of them it is the only header action.

**Decision (Symbio TASK-677):** the tour affordance goes **beside the page title**, because help is about the
page and not an action on its data. It belongs in `Birko.Web.Shell`'s header, written once, rather than in each
page. The options not chosen were the far corner (still next to Create on a CRUD page), a help or overflow menu
(none exists, and it costs a click) and a labelled button in the action group (competes with the primary
action for width). The full reasoning is in that task.

Today `BasePage.renderPageHeader()` (`Birko.Web.Shell/src/pages/base-page.ts:59`) renders
`<h1 class="page-title">` and an optional `.header-actions`, and has nowhere else to put anything.
`tour` (`TourManager`) already ships in `Birko.Web.Components` (`src/layout/b-tour.ts:78`).

## Acceptance criteria

- [x] `BasePage` gains one opt-in hook, e.g. `protected tourSteps(): TourStep[] | null` (default `null`) plus a
      stable tour id, and renders the button **beside the `<h1>`**, inside the header, only when steps exist
- [x] It is wired once in the shell and survives re-renders. Note the trap Symbio's `wireTourButton` documents:
      `this.listen` is aborted on every `update()`, so a listener wired in `onMount` dies after the first render
- [x] A launch always replays the tour (`once: false`)
- [x] The button is keyboard reachable and has an accessible name from a Shell i18n key (`bws.tour.button`,
      English default in code like every other `bws.*` key; the Shell ships no locale files, so a consumer's bundle
      supplies other languages). A bare `?` with no name fails
- [x] It reads correctly both on a page **with no** header actions and on one **with** a Create button. Check
      the read-only case first, because it is the reported symptom
- [x] Works for `BasePage`, `BaseCrudPage`, `BaseListPage` and `BaseSplitPage` through the shared header. A page
      that overrides `renderPageHeader()` can still render the button through a protected helper.
      (`BaseDetailPage` was listed here at filing; it does not extend `BasePage` and renders no page header, so it
      has nowhere to put the button. Corrected at planning, 2026-10-13.)
- [x] README documents the hook. Record the Birko.Web tag in Symbio TASK-677 so Symbio can drop its eight
      overrides — README `eec672b`; `v0.1.3` recorded in Symbio TASK-677 (`1d52094f`)

## Out of scope

- Tour content, or which pages have a tour
- Symbio's own migration of its eight pages and its custom-header pages (POS, dashboard): tracked by Symbio
  TASK-677
- The racing `input-adornment-smoke` this change exposed: TASK-584 (fixed first, `depends-on`)

## Human test plan

- [x] In the Playground (published, or `node build.js` + `node serve.mjs`), open **Layout → Page tour button
      (BasePage)**: a read-only page (Orders) above one with a Create action (Products), both with a tour. Check light
      and dark. *Judgement:* does the `?` look like it belongs beside the title on both, and is it clear what it does?
      Press it: the tour starts, and pressing again replays it.
  - **Passed 2026-10-13:** the user checked the published Playground (v0.1.3, Pages run 38381573155): the `?` sits
    beside the title on both pages, in light and dark, and the tour starts and replays.

## Implementation plan

Drafted 2026-10-13 at pick. The code lands in `Birko\Web` (`Birko.Web.Shell`); the regression suite lands in
`Birko.Web.Playground`, which is the Web packages' only test vehicle (no unit runner). This repo carries only the task.

1. **`Birko.Web.Shell/src/pages/base-page.ts`.** Add the hook and the helper:
   - `protected tourSteps(): TourStep[] | null` (default `null`), and `protected get tourId(): string`
     (default `this.localName`, which is stable per page and overridable).
   - `protected renderTourButton(): string`: `''` when there are no steps, otherwise a ghost, small `b-button` with
     `data-b-page-tour` and `aria-label` and `title` set from `bws.tour.button` (default "Take a tour"), via
     `birko-web-core`'s global `t`, as the other base pages resolve their keys.
   - `renderPageHeader()` wraps `<h1>` + `renderTourButton()` in `.page-title-group` (flex, centred, small gap,
     `min-width: 0` so a long title still wraps). `.header-actions` is unchanged.
2. **Wiring, once.** In the constructor, after `BaseComponent` has attached the shadow root, add **one raw,
   delegated** `click` listener on the shadow root that matches `[data-b-page-tour]` and calls
   `tour.start(this.tourSteps() ?? [], { id: this.tourId, once: false })`. It is not `this.listen`, which is
   aborted on every `update()`, and not `onMount`, which a subclass may override without calling `super`. A
   morph that replaces the button keeps working because the listener is delegated. Steps are read at click time,
   so a page whose steps depend on loaded state is current.
3. **Imports.** `tour` and `TourStep` come from `birko-web-components/layout`, the barrel `BaseCrudPage`'s
   `b-modal` already needs, so no new package edge and nothing new in a consumer's bundle.
4. **Docs.** Shell `README.md` (the hook, the helper, the key) and `Birko.Web.Shell/CLAUDE.md` (the "wire once,
   raw and delegated" reason, so nobody moves it to `onMount`). A `CHANGELOG.md` entry and a tag at push time,
   per `Birko\Web/CLAUDE.md`; the push itself is the user's call.
5. **Regression suite: Playground `src/page-tour-smoke.ts`**, registered in `app.ts` and in `verify.mjs`'s suite
   list. Checks, each named with what it measured:
   - a page with steps and **no** header actions: the button is inside `.page-title-group` beside the `h1`,
     **not** in `.header-actions`, and `.header-actions` is absent;
   - a page with steps **and** a header action: same placement, action group unchanged;
   - a page with `tourSteps()` returning `null`, and one returning `[]`: no button;
   - accessible name equals the `bws.tour.button` default, and the button is focusable;
   - a click after `this.update()` starts the tour (a `b-tour` overlay appears), and a second click replays it
     (`once: false`);
   - `renderTourButton()` used from an overridden `renderPageHeader()` still launches.
   Prove the guard can fail: run the suite once against the old `renderPageHeader()` (button in actions) and once
   with the listener moved to `this.listen`, and record both reds.
6. **Close out.** Record the Birko.Web tag in Symbio TASK-677 once pushed; Symbio's migration of its eight pages
   stays there.

## Progress log

- 2026-10-13 — Steps 1–4 done (uncommitted in `Birko\Web`: `base-page.ts`, Shell `README.md`, `CLAUDE.md`). Step 5:
  Playground `page-tour-smoke` 18/18 green (uncommitted in `Birko.Web.Playground`). Proved it can fail: against the
  old `renderPageHeader()` 2/7 then a throw; with the click on `this.listen` in `onMount`, 11/18 (every launch
  check red).
- 2026-10-13 — **Side effect found:** with this change `input-adornment-smoke`'s `md: text starts after the prefix`
  fails on every run (4/4), and passes on every run without it (3/3). Bisected to the `.page-title-group` wrapper,
  which only exists in pages other suites mount briefly. Cause: that check sleeps a fixed 60 ms and hopes `b-input`'s
  `ResizeObserver` has applied the padding. Waiting for the padding (up to 1 s) makes it pass 50/50 on this build,
  so `b-input` is correct and the smoke is racing. Not this task's defect; the fix is to the smoke.
- 2026-10-13 — (The "up to 1 s" above was the diagnostic probe; TASK-584's fix waits up to 2 s.) Committed: Birko.Web
  `eec672b` (`base-page.ts`, Shell README + CLAUDE.md), Playground `aa8aa24` (`page-tour-smoke`). Full verify exit 0
  on two runs with TASK-584. Close gate, independent reviewer: standards pass (the missing `bws.tour.button` in the
  Shell CLAUDE.md key list was fixed before the commit); intent: all met except the Symbio tag, which waits for the
  push; correctness: no blockers; comments: nits only (API docs and the line-guarding constructor comment kept).
  Plan step 3 was wrong to say "nothing new in a consumer's bundle": `BasePage` now imports `tour` from the
  `birko-web-components/layout` barrel as a value, so a consumer using only `BasePage` now pulls the layout barrel
  in. The package edge already existed, and there is no narrower subpath export.
  Status `verify`: the human test plan's visual judgement has not been run.
- 2026-10-13 — The test plan had no surface: the Playground gallery shows components, not pages, so the `?` only existed
  in the hidden smoke run. Added the Layout card `pg-page-tour` (Playground `6d3e63c`), two real
  `BasePage` pages. Checked in a real browser: both buttons sit in `.page-title-group`, both are named "Take a tour",
  and a click starts the tour. Verify exit 0, no EMPTY card. **Noticed, for the judgement:** the `?` (ghost, `sm`)
  is small and faint next to a 2xl title.
- 2026-10-13 — Shipped in Birko.Web `v0.1.3` (`63d085c`, pushed with the Playground; Pages run 38381573155 green).
  The user signed off the human test plan on the published Playground. Tag recorded in Symbio TASK-677. Done.
