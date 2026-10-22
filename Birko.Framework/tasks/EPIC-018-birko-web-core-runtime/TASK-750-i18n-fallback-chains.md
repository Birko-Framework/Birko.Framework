---
id: TASK-750
parent: EPIC-018
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

# I18n fallback chains (cs → sk → en)

## Context

Asked for by the Symbio session (Symbio TASK-953, Czech for the admin), passed on by the user. Symbio adds a `cs`
locale; a key with no Czech must fall back to Slovak first, then English, never to English straight away and never to
a raw key. `I18n` had one fallback (`fallbackLocale`), used in `setLocale()`, `loadModule()`, `t()` and plurals.

## Acceptance criteria

- [x] `I18nOptions.fallbackChains?: Record<string, string[]>`; a locale with no entry keeps `[fallbackLocale]`
- [x] One helper, `_chainFor(locale)`: the entry or `[fallbackLocale]`, minus the locale itself, de-duplicated
- [x] `setLocale()` and `loadModule()` load the locale and its whole chain, root and `modules/<id>/`
- [x] `t()` and plurals resolve locale → chain in order; then today's missing-key result
- [x] No behaviour change without `fallbackChains`
- [x] Tests, CHANGELOG, Core README and CLAUDE.md

## Human test plan

N/A — fully covered by automated tests (resolution, plurals, modules and the exact requests made, with and without
chains). Symbio's Czech admin is Symbio's own check.

## Outcome (2026-10-22)

- **No change without chains, asserted on the requests made.** On a locale switch, a module's chain bundle is fetched
  only if not merged yet, because before, a switch reloaded only the new locale's module bundle (the fallback's came
  with `loadModule`). Root bundles are re-fetched per switch, as the fallback's always was.
- Plurals use the rules of the locale the key is found in (sk "few" for 3 through a cs → sk chain).
- **Tests.** New Playground `i18n-chain-smoke`, 21/21:
  - cs → sk hit; cs → sk miss → en hit; a key in none returns the key; a locale without a chain uses `fallbackLocale`;
  - a module bundle loads the whole chain; plurals through the chain;
  - a chain naming the locale itself or repeating one;
  - without chains, the exact requests of `setLocale` / `loadModule` / switches, as before.
  - On the code before, all 7 chain checks fail and all 6 no-chain checks pass.
- **Isolation, measured.** The suite serves its bundles as real files (`fixtures/i18n-chain`) and reads the requests
  from Resource Timing, so it never replaces `fetch`. Run beside the others it still broke `backport-smoke`'s b-chart
  dates (`setLocale` sets the page's `lang`) and `auth-session-smoke`'s token count (its `fetch` stub saw these
  requests), and never reported. It now waits until every other suite started has reported: `window.__smokeSuites`
  from app.ts, plus the grid benchmark's last line.
- **Gate.** `verify.mjs` clean three runs in a row; entry-points 409/409; `tsc` clean. Released as Birko.Web v0.1.17.
