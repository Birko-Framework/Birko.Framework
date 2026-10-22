---
id: TASK-744
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Web.Shell: the base pages' empty-filter state is an overridable `renderEmptyFilter()`

## Context

Requested by the Symbio session (Symbio TASK-955) and relayed by the user. On a tenant with no buildings, Symbio's
building-scoped pages show "This works per building — create one first" and need a "Create a building" button inside
that empty state. `BaseCrudPage`, `BaseListPage` and `BaseSplitPage` each inline it as
`<b-card><b-empty message="…"></b-empty></b-card>`, so a consumer could add an action only by overriding `render()`.

## Acceptance criteria

- [x] `protected renderEmptyFilter(): string` on `BaseCrudPage`, beside `emptyFilterMessage`, used by the three render
      methods
- [x] No behaviour change: the default returns exactly the markup inlined before
- [x] Proven in the Playground, plus one override rendering a slot child
- [x] Docs: Shell README and CLAUDE.md; Birko.Web CHANGELOG at the release

## Human test plan

N/A — fully covered by automated tests: the default markup is compared byte for byte on all three pages, and the
override's slotted action is checked for assignment and click. Symbio verifies its own "Create a building" page.

## Outcome (2026-10-22)

- **Change.** `renderEmptyFilter()` added; the three inlined strings replaced by
  `${this.requiredFilterSet ? this.renderContent() : this.renderEmptyFilter()}`.
- **Gate.** Playground `empty-filter-smoke` 13/13.
  - On all three pages, `renderEmptyFilter()` equals the old string, the page renders it, and the content is not
    rendered.
  - With the filter set there is no empty state.
  - An override's `<b-button>` is assigned to `b-empty`'s slot and clicks.
  - On the old Shell the suite fails (`renderEmptyFilter is not a function`). `verify.mjs` stays green and `tsc`
    is clean.
- Released in Birko.Web v0.1.13 and pushed, as Symbio's image build pulls Birko.Web main.
