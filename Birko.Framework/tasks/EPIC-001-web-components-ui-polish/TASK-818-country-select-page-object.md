---
id: TASK-818
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-06
depends-on: [TASK-817]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Web.Testing: a page object that picks a country in `b-country-select`

## Context

Asked for by Symbio TASK-1068 (2026-11-06, the user chose the toolkit over a Symbio-only helper). TASK-817 put
Symbio's country fields on `b-country-select`, and three Playwright specs still drove them as text inputs:
`FormPO.fill()` types into the first inner `input`, which in the picker is the search box of the inner searchable
`b-select` / `b-multi-select` — so the value is never chosen and the save never completes.

The toolkit has `SelectPO` for a named `b-select`, but the picker's inner control carries no `name` (it is
`.country-control` inside the picker's own shadow), so no existing page object reaches it.

## Acceptance criteria

- [x] `CountrySelectPO` (Playwright lane): `pick(field, ...codes)` chooses one country (or several with `multiple`)
      through the UI — open, search by code, click the option — and asserts the picker's `.value` holds them;
      `value(field)` reads it. `field` is a b-form field path (`[data-field]`) or a locator of the picker itself.
- [x] Exposed as the `country` fixture of `withBirkoFixtures` and exported from `birko-web-testing/playwright`.
- [x] Proven by Symbio TASK-1068's three specs (they pass through it; a pick of a code the picker does not offer fails
      loudly, not by timing out). **Waived by the user (via symbio-32, 2026-11-06): the Playground check is the
      proof; Symbio runs its three specs later and reports only a page-object failure.**

## Progress

- 2026-11-06, handed over by symbio-32 (Web `task/TASK-818`, `52f085a`). Run with a real Playwright
  (Playground `country-select-po-check.mjs`, 15 checks): the single picks passed, and **every multiple pick timed
  out**. The checkbox click filtered labels with `{ has: box }`, a locator rooted at the page, which Playwright
  resolves relative to each label, so it matched nothing. And the closing Escape went to the focused search box, which
  does not close `b-multi-select` (only its `.container` handles Escape), so the open popover covered the next
  control. Fixed in `57a3f47`: click the box itself; press Escape on `.container` and check `aria-expanded`. 15/15,
  typecheck clean against Playwright's types. API unchanged.
- Spawn candidate: `b-multi-select` closes on Escape only from its box, not from its own search field or options
  (a keyboard user in the open dropdown cannot close it with Escape). Not filed.

## Out of scope

- The puppeteer lane (no consumer uses it for forms).
- Changing the picker (TASK-817).

## Outcome

Merged 2026-11-06 as Birko.Web **v0.4.9** (`4ec0e3f` merge, `f73877e` version, tag local). The user closed it on the
Playground check (`country-select-po-check.mjs`, 15/15 on main after the merge) without waiting for Symbio's specs.

The specs ran 2026-11-06: the multiple pick first failed (Escape closed the b-modal around it, [[TASK-819]], fixed in
v0.4.10); after it, all three pass 12/12 (symbio-32). The third criterion holds on measurement, not only by waiver.
