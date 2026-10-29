---
id: TASK-782
parent: null
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Ship Slovak and Czech translations with Birko.Web.Components

## Context

Found in TASK-781's human test (2026-10-29): with the Playground's locale switch finally loading its bundle on the
published page, switching to SK or CZ changed number formats and **no visible text**. Birko.Web.Components ships only
`locales/en.json` (200 strings under `bwc.*`); everything else falls back to English. The Playground carries its own
partial `cs.json` / `sk.json` (21 strings each: quantity stepper, carousel, gallery, Close), and nearly all of those are
screen-reader labels. The user asked for it to be filed as a task (`docs/BRIEF.md`, 2026-10-29).

Every Birko.Web app would get these, not only the Playground — Symbio and the other Slovak-facing consumers ship their
UI in Slovak.

## Acceptance criteria

- [ ] `Birko.Web.Components/locales/sk.json` and `cs.json` translate every key in `en.json` (200 today), same structure,
      the `{placeholder}` names kept exactly
- [ ] Wording reviewed by the user (Slovak) before merge; Czech reviewed too, or marked as a draft in the task
- [ ] `locale-keys-check.mjs` (Playground) also fails when a key in `en.json` is missing from `sk.json` / `cs.json`, or a
      `{placeholder}` differs — so a new English string cannot ship untranslated silently
- [ ] The Playground copies the library's `sk.json` / `cs.json` and drops its own partial bundles (or keeps only
      Playground-specific keys, merged on top)
- [ ] Check what is NOT driven by `bwc.*`: date / time pickers' month and day names (Intl, or `BDatePicker.setLocale`),
      the Playground's own copy (ribbon commands, card lines — English by design unless decided otherwise); record each
      as covered, out of scope, or a follow-up task
- [ ] README of Birko.Web.Components lists the shipped locales and how a consumer serves them (`setBasePath`)

## Human test plan

On the published Playground, after the push:

1. Appearance → SK. Visible text changes in at least: date picker (placeholder, Today / Clear), pagination, data table
   (search, "No data", page size), file upload (drop zone), confirm dialog (buttons), JSON diff (Tree / Side by side,
   Only changes), multi-select / tag input hints, empty state.
2. Same with CZ.
3. Read the Slovak wording on those components: natural, no English left, no broken `{placeholder}`.
4. Reload: the language stays (TASK-781's `pg-locale`).
5. Back to EN: everything English again.

## Notes

- The Playground's `subpath-check.mjs` already proves a switch loads the bundle under the Pages subpath (TASK-781).
- Birko.Web is a separate repository (`Birko\Web`); the translations land there, the check change in Playground.
