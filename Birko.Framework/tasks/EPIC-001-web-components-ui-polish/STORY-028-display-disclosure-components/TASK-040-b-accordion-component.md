---
id: TASK-040
parent: STORY-028
feature: null
status: review  # todo | in-progress | review | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-06-18
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Add a `b-accordion` (collapsible / disclosure group) component

## Context

`Birko.Web.Components` has **no standalone accordion / disclosure component**. Today consumers either
drop to the native `<details>`/`<summary>` element or reuse the *internal* collapse logic baked into
`b-form` (collapsible groups) and `b-kanban` (card collapse) — none of which is a reusable building
block. Surfaced while building the Birko.Web Playground ([[TASK-038]]), whose design-token panel had
to use raw `<details>` for its grouped sections.

Add a framework-native `b-accordion` so consumers get a tokenized, accessible, keyboard-operable
collapsible group out of the box. Follow the 10-step **New component checklist** in
`Birko.Web.Components/CLAUDE.md` (encoded in the [[new-birko-web-component]] skill).

## Acceptance criteria

- [x] New component under `src/layout/b-accordion.ts` (class `BAccordion extends BaseComponent`, `define('b-accordion', …)`), registered in `src/layout/index.ts` (and thus the barrel).
- [x] Sections provided via slots or a `setItems(...)` config (mirror the catalogue's existing patterns — e.g. `b-tabs` `setTabs`, header + body per section). → `setItems([{id,header,open?,disabled?}])` + body via `slot="{id}"`.
- [x] **Single vs multiple** expand mode via an attribute (e.g. `multiple` — default single-open, accordion-style); optional `disabled` per section.
- [x] **Accessibility**: each header is a `role="button"` (or native `<button>`) with `aria-expanded` + `aria-controls`; the panel is a labelled region; keyboard support (Enter/Space toggle, Up/Down/Home/End between headers) per the repo's `ACCESSIBILITY.md` + `dom-utils` (`isActivationKey`, `rovingIndex`). → native `<button>` headers (free Enter/Space), `<section role="region" aria-labelledby>` panels toggled via `hidden`, `rovingIndex` for Up/Down/Home/End across enabled headers. (`isActivationKey` unneeded — native button handles activation.)
- [x] Styling uses **only `--b-*` design tokens** and reuses shared `@sheet` sections where applicable (no duplicated CSS); honors the `size` attribute convention. → no shared sheet fits an accordion (backdrop/overlay/form/data-viewer/spin/sr-only) so none applied; `size` = vertical-footprint via `--b-control-min-height-*`.
- [x] Emits a kebab-case event on toggle (e.g. `toggle` with `{ id, open }`) via the BaseComponent `emit` helper.
- [x] Any user-facing text goes through `BaseComponent.label()` / `bwc.*` i18n keys (none hard-coded). → no built-in user-facing strings; headers are consumer-supplied, state conveyed via `aria-expanded`. No new `bwc.*` keys needed.
- [x] Component table + **Recent Updates** in `Birko.Web.Components` README/CLAUDE.md updated; component count bumped. → CLAUDE.md table (Layout 12→13, total 57→58) + dir comment, README usage section, API.md entry, root CLAUDE.md Recent Updates.
- [x] Tests if the project has a component test setup; otherwise a documented manual check. → no TS test runner in the project; manual check via the playground gallery (see Human test plan); playground bundles green.

## Implementation

`src/layout/b-accordion.ts` — `BAccordion`, exported as `{ BAccordion, AccordionItem }` from `src/layout/index.ts`. Added to the playground gallery (`Birko.Web.Playground/src/app.ts`, layout section) with open / collapsed / disabled sections. Full build (`node build.js`) green.

## Out of scope

- Migrating existing native `<details>` usages (incl. the playground token panel) to `b-accordion` — that's a follow-up once it lands (can be linked from [[TASK-038]]).
- Refactoring `b-form` / `b-kanban` internal collapse to reuse `b-accordion` (separate consolidation task if desired).

## Human test plan

- [ ] Add `b-accordion` to the Birko.Web Playground gallery (Layout section) — it renders with a couple of sections.
- [ ] Click + keyboard (Enter/Space, arrows) expand/collapse; `aria-expanded` flips.
- [ ] `multiple` allows several open at once; default single-open closes the previous.
- [ ] Restyles correctly under a non-light theme (token-driven).
