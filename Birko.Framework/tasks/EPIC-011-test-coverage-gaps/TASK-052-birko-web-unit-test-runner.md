---
id: TASK-052
parent: EPIC-011
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-07-06
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Adopt a web unit-test runner for Birko.Web.* (migrate backport-smoke)

## Context

The `Birko.Web.*` frontend bucket (`Birko.Web.Core` / `.Components` / `.Shell`) has **no unit-test
runner** — confirmed 2026-07-06: no `*.spec.ts` / `*.test.ts` anywhere in the Web bucket and no
vitest/jest/mocha in any of the three `package.json`s. The only automated regression coverage for the
web APIs today is `Consumers/Birko.Web.Playground/src/backport-smoke.ts` — 17 assertions run in a real
browser via the playground's `verify.mjs` (Puppeteer, gated to `/?smoke=1`). That works but is an
unusual mechanism: the checks live in a consumer app, not beside the libraries, and they only run when
someone builds + verifies the playground.

This task stands up a proper runner (**vitest** is the natural fit for the esbuild/TS setup) in the Web
bucket and migrates the backport-smoke assertions into co-located `*.test.ts`, so the web libs get
first-class unit tests like the .NET side.

**Nuance — not everything migrates cleanly:**
- Pure logic ports directly: `Formatter.duration` / Intl passthrough, `readThrough` (with a fake `fetch`).
- `MirrorStore` uses **IndexedDB** → needs `fake-indexeddb` (or a jsdom/happy-dom env) under vitest.
- `<b-sync-status>` and `BMobileAppShell` are **custom elements + shadow DOM** → jsdom's web-component
  support is limited; these may need vitest **browser mode** (Playwright provider) or stay as the
  playground browser smoke. Decide per-assertion; don't force shadow-DOM checks into jsdom if flaky.

The playground `backport-smoke.ts` + `verify.mjs` gate can be **retired once vitest covers the same
behaviours**, or kept for the genuinely browser-only bits (PWA/SW, real shadow rendering) — record which.

## Acceptance criteria

- [ ] A vitest setup in the Web bucket (root config or per-lib), wired to the existing TS/esbuild
      resolution (the `birko-web-*` path aliases), with a `test` script in the relevant `package.json`(s).
- [ ] Pure-logic + IndexedDB assertions from `backport-smoke.ts` migrated to co-located `*.test.ts`
      (Formatter.duration, readThrough online/offline/404-evict, MirrorStore via `fake-indexeddb`,
      wake-lock/audio-cue no-throw, `registerServiceWorker` best-effort).
- [ ] Custom-element/shadow-DOM assertions (`b-sync-status`, `BMobileAppShell`) either run under vitest
      browser mode or are explicitly left in the playground smoke — the split is documented.
- [ ] `backport-smoke.ts` / `verify.mjs` updated: retired for what vitest now covers, kept for the
      browser-only remainder (or removed entirely if fully covered) — no double-maintenance without a reason.
- [ ] CI/dev entry point documented (how to run the web unit tests).

## Out of scope

- Consumer E2E (that's `Birko.Web.Testing` / Playwright, a separate concern).
- The Xaml/.NET test projects (already covered by their `Birko.Xaml.*.Tests`).

## Human test plan

- [ ] N/A for the logic tests (assertions). For any browser-mode / kept-smoke bits, confirm they still
      run green via their runner (vitest browser or the playground `?smoke=1` verify).

## Implementation plan

_Populated by `/tasks plan TASK-052` — leave empty until then._
