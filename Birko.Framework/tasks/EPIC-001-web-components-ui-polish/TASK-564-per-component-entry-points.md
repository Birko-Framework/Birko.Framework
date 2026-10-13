---
id: TASK-564
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: []
pr: null  # Birko.Web 0ddd458 (v0.1.3) + 3bb1d3a (README sizes); Playground 0884d39
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

- [x] A per-component entry point: `exports` maps `./b-button` → `./src/inputs/b-button.ts` (or a
      `./components/*` pattern). Decide the shape and record it. Every shipped component is reachable,
      and a test or script fails when a new component file has no entry
- [x] Importing one component also registers the components it renders internally (e.g. `b-modal`'s
      own children), so a lean import never leaves an undefined `b-*` tag inside a shadow root. Measure
      it rather than assume: a playground or Testing check that imports each entry alone and asserts no
      `:not(:defined)` element in its render
- [x] `README.md` gets an "Import only what you use" section. It names the full barrel as the convenient
      default and the per-component imports as the lean option, with a measured before/after from one
      real consumer (Presenter TASK-035) — section `0ddd458`; Presenter's whole-app before/after added in `3bb1d3a`
      (`app.js` 642,859 → 170,662 B minified, 149,545 → 45,670 B served brotli)
- [x] Consumers' path config documented: an esbuild alias prefix and a `tsconfig` `paths` pattern, since
      consumers resolve Birko from source rather than as a package
- [x] The existing barrels and `.` keep working unchanged (not breaking → PATCH)

## Out of scope

- Switching any consumer. That's per repo: Presenter TASK-035, plus Gameshow and Symbio tasks to file there
- `Birko.Web.Core` entry points (already split into `/http`, `/i18n`, `/router`, `/state`)

## Human test plan

- [x] In Presenter, after TASK-035 adopts it: every surface renders with no bare `b-*` tags, and the bundle
  - **Passed 2026-10-13:** Presenter TASK-035 (done, `f2cb8c2`): its headless check found no undefined custom element on
    any surface through every shadow root, and the user clicked through every surface in dark and light, every
    overlay, a paired phone and the code-block slide with nothing rendered as a bare tag.
      shrinks by the amount recorded in that task

## Implementation plan

Drafted 2026-10-13 at pick. Code lands in `Birko\Web` (Birko.Web.Components); the checker lands in
`Birko.Web.Playground`, the Web packages' test vehicle. First consumer: Presenter TASK-035, which needs `b-button`,
`b-tooltip`, `b-spinner`, `b-modal`, `b-input`, `b-empty`, `b-card` and `b-code-block`.

**Decided shape (user, 2026-10-13): the category path**, which is the source file itself, e.g.
`birko-web-components/inputs/b-button`. The flat name (`birko-web-components/b-button`) was rejected because it needs 65
generated re-export files; the cost of the choice is knowing the category, which a README table answers.

Measured before deciding:
- **esbuild:** one alias, `'birko-web-components'` → `<Web>/Birko.Web.Components/src` (the directory), resolves the
  bare import (`src/index.ts`), a barrel (`src/layout/index.ts`) and one file (`src/inputs/b-button.ts`) alike.
  esbuild 0.28.2.
- **tsconfig** (`tsc` 5.9.3, `moduleResolution: Bundler`): `"birko-web-components/*": ["<Web>/Birko.Web.Components/src/*"]`
  type-checks all three forms. **Presenter's current `src/*/index.ts` pattern fails** on a per-component path (TS2307),
  so consumers must change it. A side-effect import (`import 'x'`) is not resolved at all unless
  `noUncheckedSideEffectImports` is on: a typo'd path passes `tsc` and fails only at bundle time. With the flag on, it is
  TS2307.
- **Import closure** of every component (a static scan of the `b-*` tags its own code renders, against what its imports
  define): 6 render a component they do not import. `b-checkbox` and `b-switch` render `b-tooltip` for a hint;
  `b-command-palette` renders `b-spinner` while searching; `b-confirm-dialog` renders `b-button`; `b-form` renders
  17 field controls (checkbox, date, date-range, datetime, file-upload, input, markdown-editor, multi-select,
  option-group, radio, range, search-input, select, switch, tag-input, textarea, time). Four other hits were tag names
  in comments. `b-data-table` already imports its children.

Steps:
1. **`package.json` `exports`:** one pattern per category, `"./<cat>/*": "./src/<cat>/*.ts"` for `inputs`, `layout`,
   `data`, `feedback`, `nav` and `command`. `.` and every existing barrel are unchanged (PATCH).
2. **Register what you render:** add side-effect imports to the six files above. `b-form` will pull in its 17 controls,
   which is correct: it cannot know its schema ahead. Note the size effect for a consumer importing `b-form` alone.
3. **Checker `entry-points-check.mjs`** (Playground, next to `theme-roundtrip-check.mjs`), three parts, exit 1 on a fail:
   - *coverage:* every `src/<cat>/*.ts` that calls `define('b-…')` is reachable through an `exports` pattern, and is
     listed in the README table (so a new component without an entry or a doc row fails);
   - *static closure:* each component's import closure defines every `b-*` its code renders (template literals and
     `createElement`, comments stripped);
   - *runtime:* per component, bundle a lean entry that imports only that file **through the documented alias**, mount it
     (with a fixture where children render only conditionally: a hint, a schema with every field type, an open
     dialog, a search in flight), walk all shadow roots, and assert no `b-*:not(:defined)`.
   Prove it can fail: rerun with one of step 2's imports removed.
4. **README** (Components): "Import only what you use": the barrel as the convenient default, per-component imports as
   the lean option, the path table, the esbuild alias and the `tsconfig` paths (plus `noUncheckedSideEffectImports`),
   and the measured before/after, **filled in from Presenter TASK-035** once it is recorded there.
5. **Components `CLAUDE.md`:** a component imports every component it renders (register-on-introduce), and the checker
   enforces it.
6. **Release:** a Birko.Web `CHANGELOG.md` entry and a PATCH tag at push time; the push is the user's call.

## Progress log

- 2026-10-13 — Done in Birko.Web `0ddd458` (Components) and Playground `0884d39`.
  - **Gaps the checker found beyond the plan's scan:** `label-hint.ts` renders the hint's `b-tooltip` for every
    labelled control (17 components). The plan's Python scan missed it; one import there covers all of them.
    Final set: `label-hint`, `b-checkbox`, `b-switch` (`b-tooltip`), `b-command-palette` (`b-spinner`),
    `b-confirm-dialog` (`b-button`), `b-form` (its 17 controls).
  - **Checker** `entry-points-check.mjs`: 397/397. Proved red: with the `label-hint` import, the `./command/*` export
    and one README row removed together, 311/331, each caught by its own check. After hardening, the `label-hint`
    removal alone gives 379/397. Full Playground verify exit 0. Real `tsc` 5.9.3 with the documented paths and
    `noUncheckedSideEffectImports` type-checks Presenter's eight lean imports, a barrel and `/dialogs` (exit 0).
  - **Measured** (framework code only, esbuild minify): full barrel 539,955 B / 125,335 gzip; Presenter's eight
    67,836 / 20,069; `b-form` alone 237,410 / 54,628.
  - **Close gate**, independent reviewer: standards pass (PATCH; CHANGELOG, versions and tag due with the push);
    intent: AC 1, 2, 4, 5 met and AC 3 partly met (waits for Presenter); correctness: no blockers. Fixed before the commit:
    - the tsconfig block now shows Core's paths;
    - the README says only `b-*` files are supported entry points (the pattern also reaches internal helpers);
    - the component count is corrected (54 → 64), and the wrong `b-modal` example is replaced;
    - the checker skips `import type`, leaves `'image/*'` strings alone, asserts each `b-form` control and that
      every mount ran, and reads only the README table;
    - the duplicated comments are trimmed, and the checker is listed in the Playground docs.
  - **Status `verify`:** the README's whole-app before/after and the human test plan both run in Presenter TASK-035.
- 2026-10-13 — Shipped in Birko.Web `v0.1.3`. Presenter TASK-035 adopted it (done): `app.js` 642,859 → 170,662 B minified,
  149,545 → 45,670 B served with brotli, human test passed there. The sizes are in the Components README (`3bb1d3a`,
  docs only, so no tag). Done.
