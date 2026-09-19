---
id: TASK-091
feature: FEATURE-001
parent: STORY-050
status: done
priority: P2
assignee: ai
created: 2026-07-29
depends-on: [TASK-001]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `description` — a persistent help-text row on the form controls

## Context

A form control's `hint` attribute rendered **only** as a tooltip behind a `?` icon (`renderLabel`), so there
was no way to put persistent visible text under a control. A consumer needing a value or constraint on screen
while the user types had to render its own sibling element outside the component — where `aria-describedby`
**cannot reach it**, because the real control lives in shadow DOM and nothing outside can be referenced by id.
A screen reader therefore never announced it as that field's description. That inaccessible-by-construction
workaround is the actual defect; the missing row is just how it manifests.

Live instance: Reps' `progress-page.ts` renders `<span class="hint">${goalHint}</span>` after the steps
`b-input` with a local `.hint` rule.

## Naming decision

**`description`**, over the two alternatives:

- **not `help`** — same length and initial as `hint`, adjacent in any attribute list; a review-confusion trap.
- **not a mode on `hint`** (`hint-inline`, `hint-display="tooltip|text"`) — one attribute can only have one
  presentation, so a field could never carry a standing constraint ("Max 10") *and* a longer tooltip explainer
  at once. Two independent attributes compose. A mode would also force either a default nobody wants or a
  visible change for every consumer already passing `hint`.
- **`description` names the ARIA concept it feeds** (`aria-describedby`), so the attribute and the mechanism
  agree. Longer than `label`/`hint`/`error`, but it cannot be misread.

## Acceptance criteria

- [x] `RenderFieldOptions.description`; `renderField` emits it under the control via a new
      `renderHelp(uid, description)` mirroring `renderError`, with its own `${uid}-help` id.
- [x] `aria-describedby` includes the help id whenever description text is present, via `fieldAria`. With an
      error present too, **both** are described, **error first**.
- [x] `bare` drops the row. Documented: the text becomes the control's `title` — but the **error wins**
      `title` when both are set, since it is one string and mixing an urgent failure with standing help
      muddles the important one.
- [x] Escaping settled and documented: **`renderHelp` escapes**, unlike `renderError` (callers pre-escape).
      Escape-by-default cannot produce an injection, this is new API with no back-compat pressure, and the
      caller-escapes convention is what produced three unescaped interpolations in `b-select`/`b-textarea`.
      `renderError`'s behaviour is now noted as legacy in its doc comment.
- [x] `.field .help` beside `.field .error` in `@sheet formField` — `--b-text-xs` + `--b-text-secondary`,
      same footprint as `.error` so a field's height does not jump when an error appears beside it.
      (`--b-text-muted` was the obvious "muted colour" choice and was measured and rejected — see
      "Contrast, measured".)
- [x] Wired in **all twelve** stacked-chrome controls (`observedAttributes` + `renderField` + `fieldAria`).
      Started as the seven that already used `renderField`; manual review then found that `b-time`,
      `b-date-range-picker`, `b-range`, `b-color-picker` and `b-markdown-editor` still hand-rolled the
      chrome and so had neither `bare` nor `description` — all five migrated. `b-file-upload` /
      `b-option-group` remain hand-rolled on purpose (no error row; see CLAUDE.md).
- [x] `b-form`'s `FormField` gained `description?: string`, forwarded as an attribute beside `hint`.
- [x] Docs: README § Inputs (the `hint` vs `description` table + examples + the why-not-your-own-element
      note), API.md rows on the five documented controls plus a preamble note covering all seven, and a
      CLAUDE.md convention section so the split is not re-litigated.

## Verification

- New Playground `description-smoke` harness: **55/55**. All seven render the row and reference it; none
  references it when absent (no dangling ids); error+description describe both ids in the right order and
  both resolve to real elements; visual order is control → description → error while the ARIA order is
  inverted (deliberate); `hint` and `description` coexist; markup in the text is escaped; `bare` drops the
  row, has no dangling `aria-describedby`, and falls back to `title` with the error winning when both are
  set; the attribute is reactive both ways; `b-form`'s schema key drives it; the row's colour differs from
  the error's.
- `bare-smoke` **64/64**, `form-assoc-smoke` **97/97**, `backport-smoke` **73/73** unchanged; 66 gallery
  components render, none empty, no page/console errors. `tsc --noEmit` clean on `Birko.Web.Components` and
  `Birko.Web.Playground`.
- **Density**: screenshotted the Inputs gallery, light and dark. The row is one line of `--b-text-xs` text
  directly under the control; the `b-textarea` card beside it (no `description`) is visibly unchanged. Nothing
  existing moves — the attribute is opt-in per instance — and `bare`, the dense-inline mode, drops the row
  entirely, so table cells and toolbars are unaffected by construction.

## Human test plan

- [ ] With a screen reader, focus a field carrying `description` and confirm the text is announced as the
      field's description; then set `error` as well and confirm **both** are announced, error first.
- [ ] Check a long description wraps sanely inside a narrow field (a table cell, a grid column) rather than
      forcing the column wider.
- [x] ~~Confirm the colour is legible in every shipped theme~~ — **measured instead of eyeballed**, see
      "Contrast, measured" below. Still worth a human look in `finstat`, the one theme below AA.

## Contrast, measured

The "is it legible in every theme?" item turned out to be measurable, so it was measured — computed WCAG
ratios of the rendered `.help` colour against the field background in all five shipped themes, with the
pre-existing `.error` and `label` as the baseline:

| theme | `.help` with `--b-text-muted` | `.help` with `--b-text-secondary` (shipped) | `.error` (pre-existing) |
|---|---|---|---|
| light | 2.45 **FAIL** | **7.24** AA | 4.62 AA |
| dark | 3.07 AA-large only | **5.71** AA | 3.03 AA-large only |
| neon | 2.76 **FAIL** | **4.88** AA | 5.36 AA |
| finstat | 2.03 **FAIL** | **3.77** AA-large only | 2.65 **FAIL** |
| inverse | 4.53 AA | **6.73** AA | 2.13 **FAIL** |

AA needs 4.5:1 at this size (`--b-text-xs`). The first choice, `--b-text-muted`, failed in three of five
themes — for text whose entire purpose is to be read while typing. Switched to `--b-text-secondary`, which
clears AA in four of five; it is still lighter than the label, which is larger and bolder. Font size stays
`--b-text-xs` to keep `.error`'s footprint, so a field's height does not jump when an error appears.

`finstat` remains at 3.77:1 — a limitation of that theme's `--b-text-secondary`, shared with the label, not
of this row.

## Found, not fixed (pre-existing)

**`.field .error` fails WCAG AA in three of five themes** — dark 3.03:1, finstat 2.65:1, inverse 2.13:1 (see
the table above). An error message that cannot be read is a worse problem than help text that cannot, and it
predates this task by a long way. Not touched here because fixing it means changing `--b-color-danger` per
theme, which recolours every danger affordance (buttons, borders, badges), not just this row. Worth its own
task.

## Playground

Added a `pg-description` gallery card under **Inputs** whose seven numbered rows each answer one
human-test question: SR announcement, error+description together (with buttons to set/clear the error),
long text in a deliberately narrow column, per-theme contrast (with a live theme switcher),
`hint`+`description` coexisting, `bare` dropping the row beside a chromed twin, and every stacked-chrome
control carrying it.

While building it: **the shipped themes were copied into `wwwroot/css/themes/` by `build.js` but never
linked in `index.html`**, so the whole gallery could only ever be viewed in base/light and any "does this
read correctly in theme X?" question was untestable there. Linked all four (`dark`, `neon`, `finstat`,
`inverse`) after `tokens.css`, which is what made the contrast measurement above possible.

## Follow-up pass after manual review

Reviewing in the Playground found four more things:

- **`b-search-input` at `size="sm"`/`"lg"` ran its query text under the search icon.** The size variants in
  `formControlSheet` set `padding` (shorthand), resetting the inset that clears the absolutely-positioned
  icon, and `:host([size="sm"]) input` outranks a bare `input`. Re-declared the insets per size; measured
  `padding-left` = 21px at default/sm/lg with the icon ending exactly at the text start (was 7px at sm).
- **Five controls were missing the whole attribute family** — `b-time`, `b-date-range-picker`, `b-range`,
  `b-color-picker`, `b-markdown-editor` still hand-rolled `.field`, so they had neither `bare` nor
  `description`. Migrated onto `renderField`; the set is now uniform across twelve controls.
- **`b-date-range-picker` emitted a duplicate `aria-label`.** Its endpoints already have their own
  ("Start date" / "End date"), so passing the field label as well produced two on one element. `fieldAria`
  no longer receives `label` there — and the harness's bare-mode check now asserts "keeps an accessible
  name" rather than "equals the field label", since the endpoint's own name is the more useful one.
- **`b-color-picker` in `swatch-only` mode had no ARIA at all** — the field's ARIA lived on the `.hex` box
  that mode omits, so there was no error state, no description and no name from the field. Now attached to
  the swatch in that mode.

Both ARIA bugs were caught by the harness the moment the five controls were added to its sweep, not by
reading the diff.

## Consumer follow-up (not in this task)

Reps `Reps.Web/src/pages/progress-page.ts`: move `<span class="hint">${goalHint}</span>` onto the steps
`b-input` as `description` and delete the local `.hint` rule. Reps' other `.hint`, in
`workout-exercise-edit-page.ts`, is a navigation **link** and stays page markup.


---

## Signed off (2026-09-19)

**Closed as done.** Both human-plan items turned out to be measurable, and the more interesting one
was the screen-reader item.

### The screen-reader item, moved from "the attribute points somewhere" to "the browser computed this"

`description-smoke` asserted the **wiring**: the help id is in `aria-describedby`, it resolves to a
real element, both ids appear with the error first. That is the DOM. It cannot say what an assistive
technology is *handed* — the browser computes a description from those IDREFs, and an IDREF an engine
declines to resolve across a shadow boundary gives perfect markup and a silent reader.

`a11y-description-check.mjs` asks the browser for the computed node, which is the same thing it hands
NVDA or VoiceOver. Chromium reports:

```
Too low Weight in kilograms, one decimal
```

Error first, description second — exactly what this task specified. **Mutating the single line that
pushes `${uid}-help` onto `describedBy` reds those three checks and leaves the layout check green**,
so it measures the wiring rather than its own fixture.

**It is still not a screen reader**, and the file says so: announcement order *as spoken*, verbosity
and the AT's own heuristics are not covered. The remaining value of hearing one is real but much
smaller than when the plan was written.

⚠ The a11y half is **Chromium-only, and not by choice** — `page.accessibility.snapshot()` goes over
CDP and Firefox speaks BiDi, which exposes no accessibility tree. The check reports that split rather
than running in Chromium twice and calling it cross-engine.

### The wrapping item

Measured in **both** engines: a deliberately long description inside a 120px column gives
`container=120px field=120px scroll=120px` — it wraps, and nothing widens.

### Cross-engine, now possible

`description-smoke` is **90/90 in Chromium and 90/90 in Firefox** (grown from the 55 recorded here).
Worth running for this feature in particular, since ARIA computation is exactly where engines differ.

### The contrast finding has a home

*"`.field .error` fails WCAG AA in three of five themes"* is tracked by [[TASK-130]] (P1, `todo`),
which already names `--b-color-danger-text` as the mechanism — so it is filed rather than floating in
this task's prose. `finstat`'s 3.77:1 for `.help` is a property of that theme's `--b-text-secondary`,
shared with the label, and belongs to the same task.

### Left for the consumer

Reps' `progress-page.ts` still renders its own `<span class="hint">`. That is the migration this
attribute exists to enable and it is Reps-side work, recorded in § Consumer follow-up. Not a blocker
here: the framework half is what this task owns, and the inaccessible-by-construction workaround it
was written to remove is now removable.
