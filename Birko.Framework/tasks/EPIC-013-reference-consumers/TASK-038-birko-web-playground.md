---
id: TASK-038
parent: EPIC-013
feature: FEATURE-013
status: review  # verified headlessly 2026-09-08; only the manual round-trip is left
priority: P2
assignee: ai
created: 2026-06-18
depends-on: []
blocks: []
related: [TASK-036, TASK-037, TASK-307]
pr: "Birko.Web.Playground@961bd48"
github-issue: null
jira-key: null
---

# Birko.Web playground: component gallery + live token editor + theme-CSS export

## Context

`Birko.Web.*` ships three libraries — `Birko.Web.Core`, `Birko.Web.Components` (the `b-*` Shadow
DOM catalogue, ~55 components), `Birko.Web.Shell` — but there is **no runnable app** to view the
components or tune the design system. Today you can only see a component by wiring it into a real
consumer (Symbio, gameshow, …).

Build a dedicated **Birko.Web playground** — a sibling consumer web app (consumes the libraries via
the `BIRKO_SRC` esbuild convention, same as any `Birko.Web.*` consumer; after [[TASK-036]] + the
`Birko\Web` bucket split, the frontend sources resolve to `C:/Source/Birko/Web`). Two surfaces:

1. **Component gallery** — render every `b-*` component with interactive controls to flip its
   attributes/props (variant, size, disabled, states, slots) and read its events — a Storybook-style
   catalogue, but framework-native.
2. **Live design-token editor** — edit the full `--b-*` token set and watch the gallery restyle
   live. On commit, **export the result as a CSS file you paste into your own project** as a new
   theme / starting template.

The export must line up with the framework's existing **modular theme system** (Recent Updates:
`tokens.css` base + opt-in `css/themes/*.css` + `registerThemes()`): the exported file is either a
`[data-theme="my-brand"] { … }` block (drop into the app's CSS + `registerThemes([...])`) or a
full `tokens.css`-shaped `:root` override. The playground should itself use that system to preview
(apply edits to a live `[data-theme="playground"]` block).

Suggested name: `Birko.Web.Playground`. Final name is a small open decision.

## Progress (2026-06-18)

**Scaffolded + bundling green** at `C:\Source\Birko\Consumers\Birko.Web.Playground`:
- Universal layer (`README.md`, `CLAUDE.md`, `License.md`, `.gitignore`) + `package.json` (esbuild) + `tsconfig.json` (paths → `../../Web/Birko.Web.*`) + `index.html`.
- `build.js` — upward-search resolver for the **`Birko\Web`** bucket (`BIRKO_SRC` override, else walk up to `Birko/Web/Birko.Web.Core`; no committed absolute path) + full `birko-web-*` alias map + copies `tokens.css`/`reset.css`/`themes/*` into `wwwroot/css/`.
- `npm install` + `node build.js` → **`wwwroot/app.js` (585 KB) bundles with no "file not found"**; resolved `BIRKO_SRC=C:/Source/Birko/Web`. The whole `b-*` catalogue compiles in.
- `src/app.ts` first cut: imports `birko-web-components` (registers all `b-*`); **gallery** (representative ~10 components with live attribute controls); **live token editor** (parses all 167 `--b-*` tokens from `tokens.css`, applies edits to a `[data-theme="playground"]` block live, filter); **export** (`[data-theme]` block or `:root` override, changed-tokens-only diff, copy-to-clipboard).

**Gallery expanded to the full catalogue (2026-06-18):** manifest now covers **all 60 `b-*` components** grouped by the 6 categories (inputs 22, layout 12, data 15, feedback 6, nav 4, command 1). Attribute/slot-driven components render real instances with live attribute controls (variant/size/disabled/status); property/config-driven ones (tables, chart, kanban, tree-menu, ribbon, sidebar, form, chat, overlays) are listed with an honest "needs runtime config" note. `reportMissing()` warns on any catalogue tag that didn't register; console logs `rendered N/60`. Rebuilds green (`app.js` 592 KB).

**Property-data polish (2026-06-18):** wired real sample data into **14 config-driven components** via a deferred + guarded `setup(el)` hook calling their actual data methods (discovered from source): `b-table`/`b-data-table` (`setColumns`+`setData`), `b-editable-table` (`setConfig`+`setData`), `b-chart` (`setData(ChartData)`), `b-kanban` (`setConfig`+`setCards`), `b-object-tree` (`setData`), `b-breadcrumb`/`b-tree-menu`/`b-sidebar` (`setItems`), `b-multi-select`/`b-segmented`/`b-option-group` (`setOptions`), `b-tabs` (`setTabs`), `b-chat` (`setMessages`). Shapes taken from the real exported interfaces (TableColumn, ChartData, SidebarItem, TreeMenuItem, ChatMessage, MultiSelectOption, …); a couple (EditableTableConfig, ChartSeries, KanbanColumn) are best-effort and guarded by try/catch (warns instead of breaking). Rebuilds green (`app.js` 595 KB). Components that legitimately stay noted: overlays (`b-modal`/`b-drawer`/`b-confirm-dialog`/`b-command-palette` — open on trigger), `b-tour` (programmatic), and the complex builders `b-form`/`b-ribbon`/`b-dropdown-menu`.

**Interactivity rework (2026-06-18, from browser feedback "only inputs showed"):**
- **Force-register all components** — `import * as` the barrel + reference the namespace so the bundler can't tree-shake away category modules (their `customElements.define()` side effects); guarantees all 60 register.
- **`b-ribbon` section switcher** — one panelless `RibbonTab` per category (`groups:[]`, `noPanel:true`); `tab-change` `{tab}` swaps which section the gallery shows, so it's no longer one long page.
- **Resilient rendering** — each gallery item is wrapped in try/catch (a bad component logs + skips instead of halting the section).
- **Dogfood** — the inline per-component control selects are now `b-select` (emits `change` `{value}`), not native `<select>`.
- Rebuilds green (`app.js` 603 KB).

**Token-editor UX rework (2026-06-18, from feedback "one long list / dogfood the other inputs"):**
- Tokens **grouped into collapsible sections** (Color & surface / Typography / Spacing / Radius / Sizing / Shadow / Motion / Z-index / Other) instead of one flat 167-row list; rows render **lazily** per group on first expand. Filter expands matching groups + hides non-matching rows.
- Panel chrome **fully dogfooded**: filter = `b-search-input` (`search`), export mode = `b-select` (`change`), Generate/Reset/Copy = `b-button` (native click), token value editors = `b-color-picker` / `b-input` (`change` `{value}`).
- Rebuilds green (`app.js` 607 KB).

**Section nav → `b-tabs` (2026-06-18):** the `b-ribbon` rendered an empty panel row (it's a full app-shell ribbon, not a tab strip). Switched to `b-tabs` with each category's gallery rendered **inside the tab's slot panel** — normal tab strip + content below, panels lazy-populated on first activation. Confirmed **no `b-accordion` component exists** (only internal collapse in `b-form`/`b-kanban`), so token groups stay on native `<details>` (the platform disclosure). Rebuilds green.

**Remaining for done (as of 2026-06-18):**
- Browser re-check: tabs switch sections cleanly; sections render; token groups collapse/expand; `b-*` editors restyle live; export round-trips. Report any `[playground] …` console warnings.

## Worked 2026-09-08 — the re-check is DONE, headlessly, and it found two real gaps

That item sat unticked for 82 days while the playground itself moved on considerably (it grew
`verify.mjs`, `device-fix-check.mjs`, seven smoke suites and ~1 MB of bundle), so this file was three
months stale rather than blocked. The re-check was run rather than deferred again, and two of the
criteria it covers turned out **not** to be satisfied.

### Verified headlessly — the whole of that "browser re-check" line

`node build.js && node verify.mjs && node device-fix-check.mjs`, exit 0 on both:

| measurement | result |
|---|---|
| sections switch and populate | all 6 — inputs 24, layout 13, data 16, feedback 9, nav 5, command 1 |
| components rendered | **68** |
| rendered EMPTY (no shadow content, no note) | **none** |
| catalogue tags not registered | **none** (no `reportMissing` warning) |
| token groups built | **9** |
| smoke suites | 667/667 — description 90, ribbon-overflow 16, bare 113, ribbon-scaling 44, form-assoc 104, backport 283, i18n-message 17 |
| `device-fix-check` | **68/68** |
| `[playground]` warnings | none; the only non-info lines are `grid-bench` measurements |

### ⚠ Two criteria were confirmed only by eye, and one of them was FALSE

`verify.mjs` counted sections, components and token *groups* — it never touched the token editor's
behaviour or the export, which are half of what this task is for. Driving them found:

- **Download-as-file did not exist.** The export criterion is *"copy-to-clipboard **and**
  download-as-file"*; there was no `Blob`, no `download`, no anchor anywhere in `src/`. Only the
  clipboard half had ever been built, and nothing said so. Now implemented.
- **The live-edit selector is not the one this file specifies**, deliberately and for a good reason —
  see the annotated criterion below. The code recorded the reason; this file never learned it.

Everything else held: one edit yields exactly one exported declaration (the clean-diff criterion), both
export shapes render, and the edit reaches a rendered component's computed style.

### The checks are now part of the verdict, not a printout

12 named token/export checks in `verify.mjs`, each carrying its measured value in the name (that
harness's own convention), and wired into `process.exitCode` — *a check nobody fails is not a check*.

**Mutations, disjoint:**

| mutation | red |
|---|---|
| dispatch the token's **base** value, so no edit registers | **7 of 12** — including `(#2563eb -> #2563eb)` and `0 declaration(s)`, i.e. the failure names what it saw |
| give both export shapes the same download filename | **1** — the shape-naming check, and only it |

### ⚠ Fixed a defect I had introduced in the harness itself

An earlier edit in this session left a literal **NUL byte** in `verify.mjs` (a lost backslash turned
`'\u0000'` into the character), which made `grep` treat the file as binary and would have made the next
person's search silently miss it. Removed. Worth recording because the file still ran perfectly — a
harness can be quietly corrupt and green.

### Status → `review`

Only the **round-trip into a fresh consumer** is left, which needs a second app and is genuinely human.
The two `[~]` criteria are capability-present/mechanism-different and are decisions rather than defects;
the token-editor half is [[TASK-307]].

## Acceptance criteria

### App shell
- [x] New sibling checkout with the universal layer (`README.md`, `CLAUDE.md`, `License.md`, `.gitignore`)
- [x] Consumes `birko-web-core` / `birko-web-components` / `birko-web-shell` via the `BIRKO_SRC` esbuild alias convention; `build.js` resolves the `Birko\Web` bucket (walk-up to `Birko/Web`, or `BIRKO_SRC` override) — no machine-specific absolute path committed
- [~] Links base `tokens.css` + lets the user load any built-in theme as a starting point — **the
      capability is there and the mechanism differs.** A `b-segmented` switcher applies each shipped
      theme via `data-theme` on `<html>`, and `themeTokens.get(activeTheme())` makes the active theme the
      base the export diffs against (so "as a starting point" is real). But it does **not** call
      `registerThemes()` — it sets the attribute directly. Left open rather than ticked: the criterion
      names a specific API, and whether the playground should dogfood it is a decision, not an oversight.

### Component gallery
- [x] Every `b-*` component is listed and rendered with at least one representative instance
- [x] Per-component controls to toggle the common attribute surface (`variant`, `size`, `disabled`, state attrs, key slots) and live-update the instance
- [x] Driven by a manifest derived from the catalogue so new components don't silently go missing (auto-derive where feasible; otherwise a maintained list + a check that flags components absent from the gallery)

### Token editor
- [x] Edits the full `--b-*` token set (colors, spacing, radius, typography incl. `--b-font-heading`, table/header/row tokens, status-alpha + overlay systems, z-index, the playground-relevant subset documented in `tokens.css`) — ideally the editor's token list is **parsed from `tokens.css`** so it stays in sync automatically
- [x] Edits apply live and the gallery restyles without reload — **verified headlessly** (`--b-color-primary`
      `#2563eb` → `#ff00ff` in a rendered component's computed style).
      ⚠ **Not via `[data-theme="playground"]`, and this criterion's wording is stale rather than unmet.**
      The shipped selector is `:root[data-pg-edits]`, and the code says why: live edits layer *on top of*
      the selected theme, and an element has only one `data-theme` — claiming it dropped the user back to
      light the moment they touched a token and let a theme switch wipe the edits. `(0,2,0)` beats the
      themes' `(0,1,0)`, so it still wins. The test asserts the **shipped** selector and explicitly
      asserts `data-theme` is *not* used, so nobody can "fix" it back.
- [~] Sensible editors per token kind; reset-to-base and load-from-built-in-theme actions — **half
      shipped.** Hex colours get `b-color-picker` (with the alpha slider for `#rgba`/`#rrggbbaa`),
      `#reset-btn` resets to base, and the theme switcher is the load-from-built-in action. **Not**
      shipped: `rgba()`/`hsla()` tokens and length tokens both fall back to a text `b-input`. Owned by
      [[TASK-307]] — the source comment called it a "tracked follow-up" and measurement showed nothing
      tracked it.

### Export
- [x] Export produces a valid CSS file in two selectable shapes: (a) `[data-theme="<name>"] { … }` theme block, (b) `:root` `tokens.css`-style override
- [x] Exported CSS only emits tokens that differ from the chosen base (clean diff, like the existing `dark.css`/`neon.css`/`finstat.css`), with a header comment explaining how to wire it (`registerThemes([{id,label,icon}])` + link the file)
- [x] Copy-to-clipboard and download-as-file — **download did not exist until 2026-09-08** (no `Blob`,
      no `download` anywhere); added, naming the file for the shape being exported
      (`my-brand.theme.css` vs `tokens.override.css`) because the two are wired differently at the far
      end. Both halves are now asserted in `verify.mjs`.
- [ ] ⚠ **Round-trip verified — pasting the export into a fresh consumer reproduces the previewed look.**
      The one criterion a headless harness cannot answer, since it needs a second app. Left for the
      human test plan below; everything upstream of it is measured.

## Out of scope

- Performing the `C:\Source` move — [[TASK-036]]; this task only keeps the esbuild default correct across layouts.
- A visual regression / screenshot harness for the catalogue (separate future task).
- Authoring new `b-*` components or new built-in themes — the playground showcases and configures what exists.
- Persisting saved themes server-side — export is file/clipboard based; local-storage draft is optional nice-to-have.

## Human test plan

- [x] `node build.js` resolves `BIRKO_SRC`, copies `tokens.css`, bundles the `birko-web-*` aliases with no "file not found"; app loads in a browser
- [x] Gallery shows the full catalogue; flipping a control (e.g. `b-button` `variant`/`size`) updates the live instance
- [x] Edit a token (e.g. `--b-color-primary`) → gallery components restyle immediately
- [ ] Export as a `[data-theme="my-brand"]` block; paste into a throwaway consumer + `registerThemes([{id:'my-brand',…}])` + link the file → the consumer matches the previewed look
- [x] Export as `:root` override; confirm it only contains changed tokens (clean diff vs base)
