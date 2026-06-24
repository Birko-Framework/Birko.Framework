---
id: TASK-038
parent: EPIC-013
feature: null
status: in-progress  # todo | in-progress | review | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-06-18
depends-on: []
blocks: []
related: [TASK-036, TASK-037]
pr: null
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

**Remaining for done:**
- Browser re-check: tabs switch sections cleanly; sections render; token groups collapse/expand; `b-*` editors restyle live; export round-trips. Report any `[playground] …` console warnings.

## Acceptance criteria

### App shell
- [ ] New sibling checkout with the universal layer (`README.md`, `CLAUDE.md`, `License.md`, `.gitignore`)
- [ ] Consumes `birko-web-core` / `birko-web-components` / `birko-web-shell` via the `BIRKO_SRC` esbuild alias convention; `build.js` resolves the `Birko\Web` bucket (walk-up to `Birko/Web`, or `BIRKO_SRC` override) — no machine-specific absolute path committed
- [ ] Links base `tokens.css` + lets the user load any built-in theme (`dark`/`neon`/`finstat`) as a starting point via `registerThemes()`

### Component gallery
- [ ] Every `b-*` component is listed and rendered with at least one representative instance
- [ ] Per-component controls to toggle the common attribute surface (`variant`, `size`, `disabled`, state attrs, key slots) and live-update the instance
- [ ] Driven by a manifest derived from the catalogue so new components don't silently go missing (auto-derive where feasible; otherwise a maintained list + a check that flags components absent from the gallery)

### Token editor
- [ ] Edits the full `--b-*` token set (colors, spacing, radius, typography incl. `--b-font-heading`, table/header/row tokens, status-alpha + overlay systems, z-index, the playground-relevant subset documented in `tokens.css`) — ideally the editor's token list is **parsed from `tokens.css`** so it stays in sync automatically
- [ ] Edits apply live to a `[data-theme="playground"]` block; the gallery restyles without reload
- [ ] Sensible editors per token kind (color → `b-color-picker`, lengths → number+unit, etc.); reset-to-base and load-from-built-in-theme actions

### Export
- [ ] Export produces a valid CSS file in two selectable shapes: (a) `[data-theme="<name>"] { … }` theme block, (b) `:root` `tokens.css`-style override
- [ ] Exported CSS only emits tokens that differ from the chosen base (clean diff, like the existing `dark.css`/`neon.css`/`finstat.css`), with a header comment explaining how to wire it (`registerThemes([{id,label,icon}])` + link the file)
- [ ] Copy-to-clipboard and download-as-file; round-trip verified — pasting the export into a fresh consumer reproduces the previewed look

## Out of scope

- Performing the `C:\Source` move — [[TASK-036]]; this task only keeps the esbuild default correct across layouts.
- A visual regression / screenshot harness for the catalogue (separate future task).
- Authoring new `b-*` components or new built-in themes — the playground showcases and configures what exists.
- Persisting saved themes server-side — export is file/clipboard based; local-storage draft is optional nice-to-have.

## Human test plan

- [ ] `node build.js` resolves `BIRKO_SRC`, copies `tokens.css`, bundles the `birko-web-*` aliases with no "file not found"; app loads in a browser
- [ ] Gallery shows the full catalogue; flipping a control (e.g. `b-button` `variant`/`size`) updates the live instance
- [ ] Edit a token (e.g. `--b-color-primary`) → gallery components restyle immediately
- [ ] Export as a `[data-theme="my-brand"]` block; paste into a throwaway consumer + `registerThemes([{id:'my-brand',…}])` + link the file → the consumer matches the previewed look
- [ ] Export as `:root` override; confirm it only contains changed tokens (clean diff vs base)
