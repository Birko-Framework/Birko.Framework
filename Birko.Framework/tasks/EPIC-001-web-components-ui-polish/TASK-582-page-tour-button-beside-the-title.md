---
id: TASK-582
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: todo
assignee: ai
created: 2026-10-13
depends-on: []
blocks: []
pr: null
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

- [ ] `BasePage` gains one opt-in hook, e.g. `protected tourSteps(): TourStep[] | null` (default `null`) plus a
      stable tour id, and renders the button **beside the `<h1>`**, inside the header, only when steps exist
- [ ] It is wired once in the shell and survives re-renders. Note the trap Symbio's `wireTourButton` documents:
      `this.listen` is aborted on every `update()`, so a listener wired in `onMount` dies after the first render
- [ ] A launch always replays the tour (`once: false`)
- [ ] The button is keyboard reachable and has an accessible name from a Shell i18n key (`bws.tour.button`,
      en + sk). A bare `?` with no name fails
- [ ] It reads correctly both on a page **with no** header actions and on one **with** a Create button. Check
      the read-only case first, because it is the reported symptom
- [ ] Works for `base-list-page`, `base-crud-page`, `base-detail-page` and `base-split-page` through the
      shared header. A page that overrides `renderPageHeader()` can still render the button through a
      protected helper
- [ ] README documents the hook. Record the Birko.Web tag in Symbio TASK-677 so Symbio can drop its eight
      overrides

## Out of scope

- Tour content, or which pages have a tour
- Symbio's own migration of its eight pages and its custom-header pages (POS, dashboard): tracked by Symbio
  TASK-677

## Human test plan

- [ ] In the Playground, open a read-only list page and a CRUD page that both declare tour steps, side by side.
      *Judgement:* does the `?` look like it belongs beside the title on both, and is it clear what it does?
