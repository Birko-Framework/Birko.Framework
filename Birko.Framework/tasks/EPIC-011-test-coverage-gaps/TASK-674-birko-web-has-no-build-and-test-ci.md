---
id: TASK-674
parent: EPIC-011
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-19
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Web has no build-and-test CI — type-check the libraries and build the Playground on every push

## Context

The `Birko.Web` repo (`Birko-Framework/Birko.Web`, checked out at `Birko\Web`) holds
`Birko.Web.Core`, `.Components`, `.Shell` and `.Testing`. **Its only GitHub workflow is
`token-parity`** (`.github/workflows/token-parity.yml`). That workflow is path-filtered to
`Birko.Web.Components/css/**` and checks only that the generated CSS was not hand-edited. Nothing
in CI compiles, type-checks or exercises the TypeScript. Measured 2026-10-19: `gh workflow list`
on the repo returns that one workflow, and the v0.1.11 / v0.1.12 releases (TASK-524 OAuth sign-in,
TASK-525 reorderable tabs / resizable split panels) ran no check on their code at all.

The framework repo's workflows don't cover it either. `build-and-test.yml` is .NET only.
`token-parity.yml` there checks out Birko.Web, but only to compare CSS against `tokens.json`.
Today a type error or a broken export in Birko.Web shows up only when a consumer (Symbio, the
Playground) next builds it, or in a local Playground smoke run that someone remembers to do.

**What a build means here.** The libraries publish **TypeScript source**: every `package.json`
`exports` entry points at `src/**/*.ts`, and none of them has a `build`, `test` or `typecheck`
script. Consumers bundle them with esbuild (`Birko.Web.Playground/build.js`, resolving `Birko\Web`
by walk-up or `BIRKO_SRC`). So "does it build" has two parts:

1. **Type-check each library.** Each `tsconfig.json` already sets `noEmit: true` and `strict: true`,
   and maps sibling libraries through `paths` (e.g. Components → `../Birko.Web.Core/src/...`), so
   `tsc -p <lib>/tsconfig.json` is the whole check. Measured 2026-10-19 with TypeScript 5.6.3:
   **Core, Components and Shell report 0 errors.** **Testing fails**, but only on missing peer
   packages (`puppeteer`, `@playwright/test`) and missing `@types/node` (`process`), plus one
   implicit `any` (`src/puppeteer/page-objects/form.ts:21`). Its code is fine; nothing installs
   what it imports.
2. **Bundle a real consumer.** `Birko.Web.Playground` (`Birko-Framework/Birko.Web.Playground`,
   `npm run build`) imports the libraries through their package subpaths. Building it catches what
   `tsc` cannot: a broken `exports` entry, a subpath a consumer uses that no longer resolves, an
   esbuild-only failure.

The Playground also ships browser checks (`verify.mjs` with `?smoke=1`, `entry-points-check.mjs`,
`subpath-check.mjs`, `a11y-*-check.mjs`, `keyboard-check.mjs`, `webkit-check.mjs` and others).
They are the real regression coverage for the components, but each needs a browser, so they are
the second step below, not the first.

Related: [[TASK-052]] adopts a unit-test runner (vitest) for the Web bucket. It has nothing for CI
to run until it lands. When it does, its `test` script joins this workflow, and that wiring belongs
to whichever of the two lands second.

## Acceptance criteria

- [ ] A `build-and-test` workflow in the Birko.Web repo's root `.github/workflows/`. Actions reads
      only the root directory, as `token-parity.yml`'s header records. It runs on push and
      pull_request to `main` with **no path filter**, plus `workflow_dispatch`.
- [ ] The workflow type-checks Core, Components and Shell with `tsc -p` against a **pinned**
      TypeScript version, and fails on any error.
- [ ] `Birko.Web.Testing` type-checks too: the workflow installs its peer packages and
      `@types/node` (or a dev-dependency manifest does), and the `form.ts:21` implicit `any` is
      fixed. A library left out of the check is reported in the workflow file as a deliberate
      exclusion with its reason, never dropped silently.
- [ ] The workflow checks out `Birko-Framework/Birko.Web.Playground` beside it, in the layout its
      `build.js` resolves (or with `BIRKO_SRC` set), and runs `npm ci && npm run build`. A failed
      bundle fails the job.
- [ ] **Prove the gate can fail:** on a scratch branch, an introduced type error in one library and
      a broken `exports` subpath each turn the run red, and both runs are linked in the task's
      progress log. A workflow that has only ever been seen green has not been tested.
- [ ] The Playground browser checks the step above defers (at least `verify.mjs` `?smoke=1` and
      `entry-points-check.mjs`) run in the workflow under a headless browser, **or** are filed as
      their own follow-up task with the reason they did not fit here.
- [ ] Birko.Web's `CLAUDE.md` / `README.md` say what CI runs and how to run the same checks locally.

## Out of scope

- The vitest runner itself and moving `backport-smoke` into unit tests: [[TASK-052]].
- Consumer E2E suites built on `Birko.Web.Testing` (Symbio, FisData.Stock.Web). Those run in the
  consumers' own CI.
- Publishing or versioning the packages. Releases stay tag-driven, as they are today.
- `token-parity` (unchanged).

## Human test plan

N/A — covered by automated checks. The deliverable is a CI gate, and the acceptance criteria
include proving it goes red on an introduced type error and on a broken subpath.

## Implementation plan

_Populated by `/tasks plan TASK-674` — leave empty until then._
