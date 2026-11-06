---
id: TASK-818
parent: EPIC-001
feature: null
status: in-progress
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

- [ ] `CountrySelectPO` (Playwright lane): `pick(field, ...codes)` chooses one country (or several with `multiple`)
      through the UI — open, search by code, click the option — and asserts the picker's `.value` holds them;
      `value(field)` reads it. `field` is a b-form field path (`[data-field]`) or a locator of the picker itself.
- [ ] Exposed as the `country` fixture of `withBirkoFixtures` and exported from `birko-web-testing/playwright`.
- [ ] Proven by Symbio TASK-1068's three specs (they pass through it; a pick of a code the picker does not offer fails
      loudly, not by timing out).

## Out of scope

- The puppeteer lane (no consumer uses it for forms).
- Changing the picker (TASK-817).
