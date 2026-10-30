---
id: TASK-797
parent: EPIC-001
status: done
priority: P1
assignee: ai
created: 2026-10-30
depends-on: [TASK-790]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-form keeps its field labels in the old language after a locale switch

## Context

Measured by the Symbio session on 2026-10-30 (Symbio TASK-1010, re-measured on Birko.Web v0.1.23). Since TASK-790 every
nested component relabels on a locale switch. In Symbio's blocks editor, that covers the heading, the editor's
buttons, the modal title, Cancel and Save, and the b-sortable-list label and handles. The modal stays open and the
unsaved edit is kept (TASK-791). Only the open **b-form's field labels** stay in the old language ("Title",
"Subtitle", ...).

**Cause.** The schema is plain data with strings already translated. The consumer builds it once with `t()` and
hands it to `setSchema()` (`Birko.Web.Components/src/inputs/b-form.ts:332`). b-form's own re-render on a locale
switch redraws the same strings. The Core README (`label()` section) currently names the workaround: `onI18nChange`
→ `getValues` / `setSchema` / `setValues`.

**The user ruled that this belongs in the framework, not in each consumer.** Symbio TASK-1010 is blocked on it.
Repro: Symbio `presentation/pages/blocks-editor.ts`, where `getBlockSchema(type, ...)` builds the labels with `t()`
and `form.setSchema(schema)` runs when the editor opens.

Text that a schema carries (`FormField`, `b-form.ts:80`):
- `label`, `placeholder`, `hint` and `description`;
- `options[].label` and `presets[].label`;
- the group's own title or legend;
- `ValidationRule.message` and the group rule's `message`.

b-form's built-in validation messages already go through `fmt()` at validation time, so they follow the locale.

## Open decision (preview with the user before code)

**A. A schema factory (recommended).** `setSchema(schema | (() => FormSchema))`.
- On a locale change, b-form calls the factory again and keeps the values (`getValues` → swap → `setValues`).
- It also keeps the dirty state, the collapsed groups, the focused field and the shown errors (re-validate the
  fields that had errors, so their messages switch language too).
- Consumer change is one token: `setSchema(() => getBlockSchema(type, ...))`.
- Shell already holds schemas as getters (`base-crud-page.ts:71`, `base-detail-page.ts:53`, `base-form-modal.ts:56`).
  It can pass `() => this.formSchema` at `base-crud-page.ts:872/891`, `base-detail-page.ts:217` and
  `base-form-modal.ts:159`, so every Shell page relabels with no consumer edit.
- Covers every string above, including one a consumer composes (`${t('a')} (${unit})`).

**B. i18n keys in the schema.** For example `labelKey`, or `label: { key, fallback }`, resolved by b-form at render
time.
- Every string-bearing field needs a key twin.
- A composed label cannot be expressed this way.
- Every existing schema has to be rewritten to benefit.

**C. A function per string** (`label: string | (() => string)`).
- Same coverage as A.
- But the type change touches every reader of `field.label` (validation messages, `aria-*`, Shell).

A is the smallest API change that covers everything, and plain-object schemas keep working unchanged. Decide whether
a plain object should keep today's behaviour (it stays as given).

## Decision (2026-10-30, the user)

**A and B together** ("both"): a schema factory for schemas built in code, and i18n keys in the text for schemas kept
as data. C is not built: A covers everything it would. The user asked for the universal approach after the three were
laid out. A alone covers every form built in code; only B works for a schema loaded as JSON.

## Steps

1. Preview the decision with the user, then record it here.
2. b-form: store the factory, re-run it on `onI18nChange` (subscription torn down on disconnect), swap the schema
   without losing values or UI state, and re-validate the fields that had errors. Don't fire `change` / `input`
   events for the swap.
3. Shell: pass factories from base-crud-page, base-detail-page and base-form-modal.
4. Core README: replace the `onI18nChange` → `getValues` / `setSchema` / `setValues` recipe with the factory form.
   Add API.md (b-form) and CHANGELOG entries.
5. Playground: extend `locale-switch-check.mjs`. Set up a b-form from a factory whose label, placeholder, option
   label, help text and a rule message use a key in sk.json; type a value, make one field invalid and focus a field.
   Then switch en → sk → en and assert:
   - the labels relabel;
   - the value is kept;
   - the error shows in the new language;
   - focus is kept;
   - no `change` event fires;
   - a plain-object schema is left as given.

   Re-run `rerender-leak-check.mjs` and verify.mjs.

## Human test plan

1. Playground (`node serve.mjs`, port 5180), ribbon **Lab** tab, card **Form relabels on a locale switch** (left: built
   with t() as a factory; right: i18n keys). Type into a field, leave Name empty and press Validate to show its error. Switch the locale to SK from the ribbon. The field
   labels, placeholders, select options and the error message switch to Slovak; what you typed is still there.
   Switch back to EN: same.
2. Symbio walk (TASK-1010): open the blocks editor, edit a block without saving, switch the locale. The form's field
   labels ("Title", "Subtitle", ...) relabel, the modal stays open, and the unsaved edit is kept.

## Resolution (2026-10-30)

**b-form** (`Birko.Web.Components/src/inputs/b-form.ts`):
- New `FormText = string | { key, fallback?, params? }` for every schema text property; `formText()` resolves it at
  each read (render, options, presets, rule and group messages, the `{label}` in built-in messages).
- `setSchema` also takes a factory. b-form overrides `softUpdate()`, the locale broadcast's entry point, to call the
  factory again first. With the same shape (group and field names and types) the morph keeps values and focus; with a
  different shape it re-renders from scratch and puts the values and focus back.
- Shown errors from its own rules are re-validated so they are worded again. Paths set by `setFieldError()` stay as
  given. No `change` event fires.

**Shell:** `base-crud-page` (create and edit), `base-detail-page` and `base-form-modal` pass their `formSchema` getter
as a factory.

**Docs:** Core README (the v0.1.23 recipe replaced by the factory), API.md (b-form), Components CLAUDE.md, and a
CHANGELOG entry for **v0.2.0**. It is a MINOR bump: the text properties' type changed from `string` to `FormText`,
which README § Versions counts as breaking for code that reads them. Symbio.UI compiles unchanged.

**Committed** in Birko.Web 1c6de6d (released as v0.2.0, pushed and tagged 2026-10-30) and Playground cb633eb.

**Gate:**
- `locale-switch-check.mjs` 39/39 (9 new b-form checks). With the `softUpdate` override disabled, 3 fail: the factory
  text, the re-worded errors and the changed field set. The key-form check passes either way, since keys resolve at
  render.
- `rerender-leak-check` PASS; tsc clean on Components, Shell and Symbio.UI. entry-points 427/427, locale-keys 22/22,
  a11y-name 43, a11y-description PASS.
- verify.mjs and keyboard-check are flaky on this machine today, with and without the change. verify: 0, 2 and 23
  failing with the change; 27, 4 and 29 on the baseline. keyboard: 5 failing on both, on the same code that passed
  222/222 earlier today. They are not counted as evidence either way.

**Human test, Playground (2026-10-30, the user, Birko.Web 1c6de6d in Chrome):** step 1 (Lab card, en -> sk -> en: labels, placeholder, help, option and errors relabel; typed value and the server error kept) **ok**. The Symbio walk (step 2) pending.

**Symbio walk (2026-10-30, Symbio TASK-1010 on v0.2.0):** blocks-editor passes `setSchema(() => getBlockSchema(...))`. With a Hero block open and an unsaved title, switching to Slovak relabels the fields ("Titulok", "Podtitulok"); the modal stays open ("Upraviť blok", "Uložiť"); the typed title is kept; no console errors. Symbio spec presentation-block-locale.spec.ts is green, and red with an object schema; the 30 other block-editor specs are green; Symbio tsc clean. **PASS.**
Closed by the user 2026-10-30.
