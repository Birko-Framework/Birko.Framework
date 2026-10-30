---
id: TASK-801
parent: EPIC-001
status: in-progress
priority: P1
assignee: ai
created: 2026-10-30
depends-on: [TASK-797]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Shell pages keep their entity name and modal title in the old language after a locale switch

## Context

Asked for by the Symbio session on 2026-10-30, ruled by the user (Symbio TASK-966). Measured on v0.1.23:
Communication → Notifications, switch to Slovak, the page's h1 stays "Notifications".

`entityLabel` is a plain property in `base-crud-page.ts:77`, `base-detail-page.ts:58` and `base-form-modal.ts:61`.
`BaseListPage` and `BaseSplitPage` inherit the crud one. Symbio's 126 CRUD pages set it as a class field,
`protected entityLabel = t('modules.communication.notifications')`, which runs once when the page is created.
`pageTitle` (`base-crud-page.ts:417`) returns it, and `{entity}` in toasts reads it, so both stay in the creation
locale. A subclass cannot override a property with a getter (TS2611). `BasePage.pageTitle` already has the
accessor shape (`base-page.ts:64`), and Symbio overrides it as a getter on its dashboards.

**Found while looking:** base-crud-page and base-form-modal render `<b-modal id="modal" title="">` and set the title
in code when the modal opens (`modal.setAttribute('title', this.t('bws.common.new' | 'bws.common.edit'))`). A locale
switch re-renders the page, and the morph puts the rendered `title=""` back, so an open modal's title goes blank
rather than translating.

## Fix

- `entityLabel` becomes an accessor in all three bases: a getter returning a stored value (default `'Item'`) and a
  setter storing it. A subclass then writes `protected override get entityLabel() { return t('…'); }`.
- **BREAKING (MINOR, v0.3.0):** a subclass that still declares `protected entityLabel = '…'` as a field gets TS2610
  ("defined as an accessor in class …, but is overridden here as an instance property"). Migration: write a getter.
  Symbio sweeps its 126 fields to getters.
- The modal title is rendered from state (create or edit) instead of being set once in code, so it follows the locale
  and survives the page's re-render.

## Human test plan

1. Playground (`node serve.mjs`, port 5180): on a Shell CRUD page whose `entityLabel` is a getter using `t()`, switch
   the locale. The page heading changes language. Open New, switch again: the modal title changes language and the
   modal stays open.
2. Symbio walk (TASK-966): Communication → Notifications, switch to Slovak. The h1, an open create or edit modal's
   title and an `{entity}` toast are in Slovak.

## Resolution (2026-10-30, in progress on branches)

Developed on branch `task/TASK-801` in worktrees (`C:\Source\Birko\Web.TASK-801`,
`C:\Source\Birko\Consumers\Birko.Web.Playground.TASK-801`). Symbio builds against the `C:\Source\Birko\Web` checkout,
and the change breaks its 126 field declarations until its sweep lands, so it reaches `main` only together with
Symbio's sweep, by agreement with the Symbio session.

- `entityLabel` is an accessor (getter over a private `_entityLabel`, plus a setter) on BaseCrudPage, BaseDetailPage
  and BaseFormModal.
- New `modalTitle` getter (crud) renders the create / edit title from `_editingId`. It is used by BaseCrudPage,
  BaseListPage and BaseSplitPage, which each render their own modal; BaseFormModal renders it inline.
- Docs: Shell README examples are now getters, a Shell CLAUDE.md rule, CHANGELOG **v0.3.0** (BREAKING, with the
  migration), and package versions 0.3.0.
- Playground: new `/?crud=1` fixture (`src/crud-locale-fixture.ts`); its `entityLabel = 'Item'` lines are dropped (the
  default); sk.json gains `pg.crud.entity` and `bws.common.new`.

**Gate:**
- `locale-switch-check` 43/43 on the branch. Built against `main` the 4 new checks fail: the base field shadows the
  getter ("Item"), and the open modal's title goes blank.
- verify.mjs 0 failing twice; rerender-leak PASS; entry-points 427/427; locale-keys 22/22; a11y-name 43; Shell tsc
  clean.

**Other consumers with the field form, to migrate when this lands:** Symbio (126; it is preparing the sweep on a branch) and
gameshow-app (3 admin pages: fursuits, games, teams). The framework's `new-birko-web-page` skill template is updated
in the same push.

**Branches, committed 2026-10-30:** Birko.Web `task/TASK-801` 612467c, Playground `task/TASK-801` 64b0e77, gameshow-app
`task/birko-v0.3.0` 522018c (off `dev`; the three admin pages use a getter over `gameshow.entity.*`, their `pageTitle`
overrides go; type-checked against the branch with no entityLabel errors, and built). Symbio's sweep: branch
`task/TASK-966` 67f2b7f6, type-checked clean against the branch. Waiting on Symbio's "go" (its TASK-1010 gate run builds
from `C:\Source\Birko\Web`). Then: merge to main, tag v0.3.0, push (the user's word), update the `new-birko-web-page`
skill template (`entityLabel` as a getter), merge the Playground and gameshow branches, and tell Symbio.
