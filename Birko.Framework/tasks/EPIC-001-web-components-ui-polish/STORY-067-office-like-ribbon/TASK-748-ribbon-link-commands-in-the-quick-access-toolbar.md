---
id: TASK-748
parent: STORY-067
feature: FEATURE-019
status: verify  # verified with real clicks and keys 2026-10-22; the human test plan is left
priority: P2
assignee: ai
created: 2026-10-22
depends-on: [TASK-737]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: link commands in the Quick Access Toolbar

## Context

Asked for by the ClientApi.CSharp session (the FinStat web tester), on the user's behalf: the user wants the
Quick Access Toolbar in the tester, and every endpoint command there is a link (`#/cz/detail/elite …`), as in most
navigation ribbons. TASK-737 excluded links: "Add to Quick Access Toolbar" never appeared on an `href` item. The only
reason recorded was that toolbar buttons were `<button>`s.

## Acceptance criteria

- [x] A link command (`href`, no `action`) can be added, and an app can give one as a default
- [x] In the toolbar it is a link to the same address, so middle-click and "open in new tab" work
- [x] A click follows it and emits `item-click` with `source: 'qat'` and `tabId` / `groupId` / `itemId`
- [x] Arrow keys and Enter work as on the other toolbar buttons; a disabled link neither navigates nor reports
- [x] Input boxes stay excluded; README and CLAUDE.md updated

## Human test plan

- [ ] In the FinStat tester: add an endpoint to the toolbar, click it, middle-click it (new tab), reload (kept)

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground, not pushed (not in v0.1.14).

- A link command renders as `<a class="ribbon-qat-btn" href=…>`, with border-box sizing and no underline, so it is
  drawn exactly as a button (measured: same height, padding, minimum width and colour).
- Space does not activate it: it is a link, and links activate with Enter.
- **Gate.** Playground `keyboard-check.mjs` 203/203, with 8 new checks:
  - an app's default link, the "Add" offer on a link command, and adding it as a link to the same address;
  - drawn as a button; a click follows it and reports with its tab, group and command;
  - arrows reach it; Enter follows it and reports once; a disabled one neither navigates nor reports.
  - They fail on the code before. `ribbon-a11y-check` 22/22; `verify.mjs`'s only failure was `carousel-smoke`
    (TASK-745).

## Checked in the FinStat tester (2026-10-22)

By the ClientApi.CSharp session, against the tester built from BIRKO_SRC (automation, not a person):

- Right-click on a link command offers "Add to Quick Access Toolbar".
- The added command (CZ Premium) is `<a class="ribbon-qat-btn" href="#/cz/detail/premium">`.
- Its ref is saved under `birko-ribbon-qat:finstat-tester` and survives a reload.
- A click navigates.

Middle-click (a new tab) was not tested, so the human test plan stays open on that one step.

