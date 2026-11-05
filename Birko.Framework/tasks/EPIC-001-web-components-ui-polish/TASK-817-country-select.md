---
id: TASK-817
parent: EPIC-001
feature: null
status: done  # signed off by the user in Symbio 2026-11-05
priority: P2
assignee: ai
created: 2026-11-05
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-country-select: a country picker whose value is ISO codes

Asked for by the user through the Symbio session (symbio-32, 2026-11-05). Every Symbio country field is an ISO code
typed by hand (`type: 'tags'` or a text input), which the user finds unfriendly and which lets "Slovakia" or "SVK" in.
The "PPL do Evropy" shipping method holds 27 codes typed one by one.

## Acceptance criteria

- [x] `b-country-select`, one or several (`multiple`). The value is upper-case ISO 3166-1 alpha-2 codes, with the
      `b-select` / `b-multi-select` contract (`.value` a comma string, `getSelected` / `setSelected`, `change`).
- [x] Shown as "🇸🇰 Slovensko (SK)": `Intl.DisplayNames` in the current i18n locale, no name table. A built-in list of
      the 249 assigned codes.
- [x] Searchable by name and by code, accents ignored.
- [x] `countries` (only these) and `preferred` (these first).
- [x] Group shortcuts with `multiple`: EU (27) by default, extensible (`registerCountryGroup`, `groups=`).
- [x] `b-form` `type: 'country'` (+ `multiple`, `countries`, `preferredCountries`, `countryGroups`).
- [x] `countryName(code, locale?)` for read-only places.
- [x] Human test in the Playground (PASS, the user, 2026-11-05) and in Symbio. **Symbio PASS (the user, 2026-11-05).**

## Decisions made here (the API was left to this task)

- One tag with a `multiple` attribute, rendering a searchable `b-select` or `b-multi-select` inside, rather than a
  second picker implementation: it looks and behaves as they do. The inner control's `change` is stopped and replaced
  by one carrying codes, so a page gets exactly one.
- The shortcut row is a general `b-multi-select` feature (`setQuickPicks`), not country-only.
- `b-multi-select`'s search now ignores accents (`matchesSearch`, as `b-select` already did): "reunion" finds Réunion.
- A stored value that is not a code (an old row holding "Slovakia") stays selected and is shown as itself, so saving
  a form does not lose it. Nothing guesses a code from it: Symbio checks its data before switching a field.

## Out of scope

- Switching Symbio's fields (a Symbio task; the Symbio session owns it).
- Subdivisions, phone prefixes, currencies.

## Human test plan

- [x] Playground → Gallery → Inputs → **Country select**: the box shows 🇸🇰 Slovakia (SK); typing "slov", "sk" and
      "reunion" finds Slovakia / Réunion; the `multiple` control shows chips and an **EU (27)** button that adds the 27
      and, pressed again, removes them. Switch the locale to Slovenčina: names change, the selection stays.
- [x] Symbio, once its session switches a field (e.g. a shipping method's allowed countries): the same.

## Built (2026-11-05), on branches, not merged

- Birko.Web `task/TASK-817` (worktree `C:\Source\Birko\Web-TASK-817`): `inputs/countries.ts` (codes, EU group, helpers),
  `inputs/b-country-select.ts`, `b-multi-select` quick picks and folded search, `b-form` `type: 'country'`, docs
  (API.md, README.md, CLAUDE.md inventory), `bwc.country.groupEu`.
- Playground `task/TASK-817` (worktree `C:\Source\Birko\Consumers\Birko.Web.Playground-TASK-817`): `country-select-smoke`
  (29 checks) and a gallery card. verify.mjs 0 failing; entry-points-check 433/433; a11y-name, a11y-description,
  locale-keys, rerender-leak, subpath and keyboard checks clean. `tsc --noEmit` on Birko.Web.Components clean.
- Proven able to fail: with the inner `change` let through, two checks go red (two events per pick).

**Playground: PASS (the user, 2026-11-05)**, on the branch build. Locale switch measured there: EN Slovakia / SK
Slovensko / CS Slovensko, the list re-sorted per language, the value unchanged. The card first sat narrow in multiple
mode: the gallery's stretch list lacked the tag (Playground 4ca756c); the component fills a block container (measured).
The shortcut's "EU" text has only an English key (`bwc.country.groupEu`); "EÚ" for sk/cs is open.

## Merged (2026-11-05)

Birko.Web main a79cea1 (merge 1ded891), tagged **v0.4.8** locally; Playground main 385d9e0. Not pushed. Worktrees and
branches removed. The Symbio session (symbio-32) switches Symbio's fields in its own task.

## Closed (2026-11-05)

The user: it works in Symbio, after the Symbio session switched its country fields. Birko.Web v0.4.8 is pushed.
