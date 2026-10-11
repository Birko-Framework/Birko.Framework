---
id: TASK-564
parent: EPIC-001
feature: null
status: todo
priority: P2
assignee: null
created: 2026-10-11
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A consumer can't import just the components it uses: the only entry points are whole barrels

## Context

Found from Presenter's Lighthouse audit (Presenter TASK-025 → TASK-035, 2026-10-11). Presenter does
`import 'birko-web-components'` and ships every component: a 637 KB minified bundle, for 8 components
actually used. `package.json` `exports` offers `.` and six category barrels (`./inputs`, `./layout`,
`./data`, …). Each barrel still drags in its heaviest members. `README.md` § dialogs already says the
`layout`/`inputs` barrels "roughly double the bundle", which is why `dialogs` is a lean subpath. The same
fix was never offered for components in general.

Consumers importing the full barrel today (checked 2026-10-11): **Presenter** (`src/app.ts`),
**Gameshow** (`Gameshow.Web/src/admin/index.ts` and `src/control/index.ts`) and **Symbio**
(`Symbio.UI/src/app.ts`). Each consumer's own switch is a task in that repo, not here.

Every component file calls `define('b-…', Class)` at module scope, so a component is registered by
importing its file: a side-effect import per component works without any new API.

## Acceptance criteria

- [ ] A per-component entry point: `exports` maps `./b-button` → `./src/inputs/b-button.ts` (or a
      `./components/*` pattern). Decide the shape and record it. Every shipped component is reachable,
      and a test or script fails when a new component file has no entry
- [ ] Importing one component also registers the components it renders internally (e.g. `b-modal`'s
      own children), so a lean import never leaves an undefined `b-*` tag inside a shadow root. Measure
      it rather than assume: a playground or Testing check that imports each entry alone and asserts no
      `:not(:defined)` element in its render
- [ ] `README.md` gets an "Import only what you use" section. It names the full barrel as the convenient
      default and the per-component imports as the lean option, with a measured before/after from one
      real consumer (Presenter TASK-035)
- [ ] Consumers' path config documented: an esbuild alias prefix and a `tsconfig` `paths` pattern, since
      consumers resolve Birko from source rather than as a package
- [ ] The existing barrels and `.` keep working unchanged (not breaking → PATCH)

## Out of scope

- Switching any consumer. That's per repo: Presenter TASK-035, plus Gameshow and Symbio tasks to file there
- `Birko.Web.Core` entry points (already split into `/http`, `/i18n`, `/router`, `/state`)

## Human test plan

- [ ] In Presenter, after TASK-035 adopts it: every surface renders with no bare `b-*` tags, and the bundle
      shrinks by the amount recorded in that task
