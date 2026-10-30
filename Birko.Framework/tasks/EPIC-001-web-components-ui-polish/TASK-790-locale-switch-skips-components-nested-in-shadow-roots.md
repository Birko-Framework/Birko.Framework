---
id: TASK-790
parent: EPIC-001
status: todo
priority: P1
assignee: ai
created: 2026-10-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A locale switch never re-localizes a component nested in another component's shadow root

## Context

Measured by the Symbio session on 2026-10-30 (Symbio TASK-1010, blocked on this by the user's ruling "framework
only"; Symbio has no local workaround and verifies once it ships).

`BaseComponent.onGlobalChange` (`Birko.Web.Core/src/base/base-component.ts:35-50`) soft-updates on a global change (the
locale switch, wired at `:413`) **only** instances whose `getRootNode()` is the `Document`. The comment's reasoning:
shadow-DOM children are kept by the parent's morph and re-render through `attributeChangedCallback` when an observed
attribute changes; soft-updating them as well would add duplicate listeners and strip dynamic classes. But a nested
component whose text comes from `t()` / `label()` sees no attribute change, so it never re-renders.

**Measured in Symbio:** the pages page heading went "Pages" → "Stránky", while its nested `s-blocks-editor` kept every
text in English: Add Block, the open modal's title, Create / Cancel, the form's field labels, the rows' Edit. Symbio has
seven such nested components (s-blocks-editor, s-translation-editor, s-dashboard-grid, s-connection-builder,
s-chat-window, s-chat-trigger, s-conflict-modal). Framework components nested in them are hit too (b-sortable-list's
handle labels, b-sortable-picker's defaults).

## Fix direction (to settle in the plan)

- Re-render nested instances safely: e.g. parent-first (each document-level instance, after its soft-update, cascades
  to the BaseComponents in its own shadow root), or a per-instance locale subscription, without the duplicate
  listeners or stripped classes the skip was guarding against. Measure both hazards before and after.
- A consumer's form whose schema was built with `t()` at open time (`b-form` `setSchema`) still holds old strings:
  give consumers a hook (e.g. an overridable `onLocaleChange()` on BaseComponent, or an event) or documented guidance
  to rebuild it.

## Steps

1. Reproduce in a Playground smoke suite: a BaseComponent nested in another's shadow root, with a `t()` text and a
   `label()` text; switch locale; assert it changed (red today). Include a framework component nested two levels deep.
2. Fix; assert no duplicated listeners (a click fires once) and dynamic classes survive; mutation-prove.
3. Run verify.mjs and the locale / keyboard / device gates; API.md (Core) note, CHANGELOG, version bump.
4. Tell the Symbio session (TASK-1010).

## Human test plan

Playground: open a card with a nested component (e.g. the sortable picker inside a modal), switch the locale, check
every text changed and the controls still work once per click. Symbio: Presentation → Pages → a page, switch to
Slovak; the blocks editor, its open modal, buttons and form labels all switch (Symbio TASK-1010).
