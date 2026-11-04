---
id: TASK-812
parent: EPIC-001
feature: null
status: done
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

# BAppShell can turn on the ribbon's Quick Access Toolbar, with a storage key the app chooses

## Context

For Symbio TASK-1026 (the Quick Access Toolbar, kept per user and tenant). `b-ribbon` has the toolbar (`setQuickAccess`,
`qat-key`), but an app on `BAppShell` cannot turn it on reliably: `qat-key` set on `#ribbon` from outside is **removed by
the shell's next re-render** — `BaseComponent`'s morph drops every attribute the fresh template does not carry
(`base-component.ts:390`) — and the ribbon then reloads with no key, so the user's choice is neither read nor saved.

## Acceptance criteria

- [x] `BAppShell` has `protected get ribbonQuickAccessKey(): string | null` (default `null`: no toolbar, as today) and
      `protected get ribbonQuickAccessDefaults(): RibbonItem[]` (default `[]`: an empty toolbar the user fills).
- [x] With a key, the template renders `qat-key` (so a morph keeps it) and `refreshRibbon()` turns the toolbar on once and
      keeps the attribute equal to the getter — a changed key (another user, another tenant) loads that key's own choice.
      Back to `null` turns the toolbar off.
- [x] README (Shell § app shell) documents the two getters; CHANGELOG entry; PATCH release.

## Implementation notes

Birko.Web v0.4.4 (local commit + tag; pushed on the user's word). `b-app-shell.ts`: the two getters, `qat-key` rendered by
`_qatKeyAttr()`, and `_applyQuickAccess()` called from `refreshRibbon()` (which `onUpdated` runs every render) — key first,
then `setQuickAccess(defaults | null)` only when on/off changes. README § *The ribbon's Quick Access Toolbar*.
Tests: no Playground smoke yet — covered end to end by Symbio `tests/ui-e2e/ribbon-quick-access.spec.ts` (add from the
context menu, the key names user and tenant, survives a reload, removed again); red with the key `null`. A Playground smoke
is the open follow-up if wanted.

**Fix found in Symbio's human test (same v0.4.4, `111f760`, tag moved — it was never pushed):** the shell kept its own
`_qatOn` flag, and the brand link (→ dashboard) re-mounts the shell, which builds a NEW `<b-ribbon>`; the flag said
"already on", so the new ribbon had its key but no toolbar until a reload. On/off is now read from the ribbon
(`BRibbon.quickAccessEnabled`). Red then green: Symbio `ribbon-quick-access.spec.ts` (System tenant, brand-link step).
