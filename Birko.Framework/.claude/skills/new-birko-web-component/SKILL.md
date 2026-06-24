---
name: new-birko-web-component
description: Add a new `b-*` web component to `Birko.Web.Components` (the framework-side component library, not a consumer app). Use when the user says "novy birko web komponent", "novy b-* component", "add b-foo", "new birko component", "pridaj komponent do birko-web-components", or similar requests to extend the Shadow DOM component catalogue. Encodes the 10-step "New component checklist" from `Birko.Web.Components/CLAUDE.md`: file under `src/{category}/b-{name}.ts`, class `B{Name} extends BaseComponent`, `define()` registration, shared-sheet reuse (never duplicate CSS), mandatory `--b-*` design tokens, semantic HTML, kebab-case custom events, `size` attribute convention (vertical-footprint / text-scale / width / shape / inline-chip), global i18n via `BaseComponent.label()`, and updating the component table + `Recent Updates`. Companion: [[new-birko-web-page]] (consumer-side page using these components).
---

# Birko.Web.Components — New `b-*` Component

Add a new Shadow DOM web component to `Birko.Web.Components` — the framework library consumed by Symbio UI, FisData, and any project importing `birko-web-components`. The catalogue currently has 54 components across inputs / layout / data / feedback / nav / command, all following one tight pattern.

## Authoritative references — READ THESE FIRST when invoked

- `C:\Source\Birko\Web\Birko.Web.Components\CLAUDE.md` — **the spec**. Contains the directory structure, the "New component checklist" (10 steps), the mandatory token + shared-sheet rules, the `size` attribute taxonomy (five categories), and the live component inventory table. **If anything below disagrees with that file, follow the file.**
- `C:\Source\Birko\Web\Birko.Web.Components\src\shared-styles.ts` — the shared `@sheet` exports (`backdropSheet`, `overlayHeaderSheet`, `formFieldSheet`, `formControlSheet`, `dataViewerCardSheet`, etc.). Browse this before writing any CSS.
- `C:\Source\Birko\Web\Birko.Web.Components\css\tokens.css` — full `--b-*` token catalogue (light + dark theme).
- `C:\Source\Birko\Web\Birko.Web.Core\src` — `BaseComponent`, `i18n` global, `useI18n`, `onI18nChange`, `t(key, params, fallback)` (loaded via `BaseComponent.label()`).
- A **reference component close to what you're building** — pick one and mirror its file shape:
  - Inputs → `src/inputs/b-date-range-picker.ts` (recent, 21st input, mature pattern with static `setLocale`, complex internal state)
  - Layout → `src/layout/b-modal.ts` or `b-drawer.ts` (overlay sheet reuse)
  - Data → `src/data/b-kanban.ts` (recent, recursive nesting + DnD) or `b-json-viewer.ts` (composes other components)
  - Feedback → `src/feedback/b-toast.ts` (manager pattern)
  - Nav → `src/nav/b-sidebar.ts` (resize + collapse)

## Inputs to gather from the user

1. **Tag name** — `b-{kebab-case}`. Check `src/index.ts` for collisions.
2. **Category** — `inputs` / `layout` / `data` / `feedback` / `nav` / `command`. Pick the one whose existing peers your component is closest to.
3. **Purpose in one line** — used in the description above the class and in the `Recent Updates` entry.
4. **Public API surface** — attributes (declarative config), methods (imperative control), events (kebab-case), slots (content projection). Sketch these *before* coding — they're the contract.
5. **`size` category** (if the component will expose `size`) — pick one of the five from CLAUDE.md § "size attribute convention":
   - **Vertical footprint** — `min-height` of chrome (form inputs)
   - **Text scale** — inner `font-size` only (viewers, code blocks)
   - **Width** — `max-width` of panel (modal, drawer)
   - **Shape weight** — diameter / track (spinner, progress)
   - **Inline chip / button** — `padding` + `font-size` (button, badge, tag)
6. **Shared sheets needed** — scan the table in CLAUDE.md § "Shared stylesheets" and decide which apply. **Default to using a shared sheet** rather than writing CSS — that's the rule.
7. **i18n keys** — every user-facing string goes through `BaseComponent.label(attrName, i18nKey, englishFallback, params?)` using a `bwc.{component}.{purpose}` key. Pre-decide the key namespace.

## File layout (the 10-step checklist)

The canonical checklist lives in `Birko.Web.Components/CLAUDE.md` § "New component checklist". Re-read it. Summary:

1. **File** — `src/{category}/b-{name}.ts`
2. **Class** — `export class B{Name} extends BaseComponent { … }`
3. **Register** — `define('b-{name}', B{Name})` at bottom of file (uses the helper that avoids redefining if HMR re-runs)
4. **Export** — add to `src/{category}/index.ts` AND `src/index.ts`
5. **Check shared sheets** before writing CSS; use `static get sharedStyles() { return [sheet1, sheet2]; }`. Only put **unique** styles in `static get styles()`.
6. **`--b-*` tokens exclusively** — never literal `#fff`, `12px`, `4px`. See `css/tokens.css`.
7. **Semantic HTML** — `<header>`, `<footer>`, `<section>`, `<article>`, `<p>`, `<time>`, `<h2>`–`<h6>`, `<dialog>`, `<nav>`, `<kbd>`. When using `<p>` / `<h*>` / `<ul>`, reset browser default margins in CSS.
8. **Typed `CustomEvent`** — emit via `this.emit('event-name', detail)` (kebab-case). Always `composed: true` + `bubbles: true` (the `emit` helper handles this).
9. **`aria-hidden="true"`** on purely decorative elements.
10. **Update CLAUDE.md** — add a row to the component inventory table in `Birko.Web.Components/CLAUDE.md` matching the format of existing rows (Tag, Class, Key methods, Key attributes).

## The `size` convention (deeper)

Always style via `:host([size="sm"])` / `:host([size="lg"])` selectors — **never** via class interpolation (`class="${size}"`). The host-attribute pattern:

- Keeps `size` a pure CSS switch (no `observedAttributes` entry needed, no re-render on change)
- Stays consistent across the library

If your component's `size` doesn't fit any of the five categories cleanly, **discuss with the user before inventing a sixth** — the taxonomy was deliberately constrained.

## i18n (mandatory)

Every label / button / placeholder / error message goes through global i18n. The pattern:

```typescript
this.label('label-confirm', 'bwc.dialog.confirm', 'Confirm')
```

- First arg: the host attribute name an app can use to override per-instance (`<b-foo label-confirm="OK">`).
- Second arg: the `bwc.*` i18n key (canonical English in `src/locales/en.json`).
- Third arg: hard-coded English fallback if neither attribute nor i18n match.
- Fourth arg (optional): `{param}` placeholder interpolation map.

Add new keys to `src/locales/en.json` under the `bwc.{component}.{purpose}` namespace.

Because `BaseComponent` auto-subscribes to `onI18nChange`, your component re-renders automatically when the app calls `setLocale()` — no manual wiring needed.

## Component public API pattern

Per CLAUDE.md § "Component public API pattern":

1. **Attributes** for declarative configuration (string, boolean, number) — registered in `static get observedAttributes()`
2. **Methods** for imperative control (`open()`, `close()`, `setData()`, `setConfig()`, `load()`)
3. **Custom events** (composed + bubbles) for reactivity — `this.emit('event-name', detail)`
4. **Slots** for content projection

Never expose internal state as properties — keep Shadow DOM encapsulation. The host element's attributes + methods + events + slots are the entire contract.

## Documentation updates

Per [[feedback_update_docs]] and CLAUDE.md "Recent Updates" cadence:

1. **Component inventory table** in `Birko.Web.Components/CLAUDE.md` — add a row.
2. **`src/locales/en.json`** — add the new `bwc.*` keys.
3. **Root `C:\Source\Birko\Framework\Birko.Framework\CLAUDE.md`** § "Recent Updates" — add a `### Birko.Web.Components — b-{name} (YYYY-MM-DD)` entry describing what the component does and any notable patterns. Use [[roll-changelog]] when that section gets long.
4. **`README.md`** — update the components-by-category list if you add a notable component.
5. **API.md** in `Birko.Web.Components/` (if present) — add the new component's API surface.

## Tests

`Birko.Web.Components` ships without a dedicated test runner today (the catalog is verified by visual usage in consumer apps like Symbio UI). If you add a test framework, follow the Birko convention (xUnit/FluentAssertions doesn't apply to TS — pick Vitest or Playwright per the user's preference) and create a sibling `Birko.Web.Components.Tests` project per [[new-birko-subproject]].

For now, **verify visually in a consumer app** — spin up the Symbio UI dev server or use the in-repo demo page if one exists.

## What this skill does NOT do

- It does not modify consumer apps. After the component ships in `Birko.Web.Components`, consumer apps pick it up automatically via the source-only import (bundled by esbuild with `BIRKO_SRC`).
- It does not write the component's actual logic. The skill is the scaffolding + convention checklist; the user implements the behavior.
- It does not handle responsive variants — use shared `@media` patterns from existing components rather than inventing a new breakpoint system.
