# Birko.DesignTokens — CLAUDE.md

Single-source design-token generator. `tokens.json` is the source of truth; a C# tool emits the
web CSS (byte-identical) and the Avalonia AXAML dictionaries. Part of **EPIC-015 (Birko.Xaml)**,
delivered by **STORY-029**.

See `README.md` for usage. This file records the conventions and non-obvious decisions.

## Scope

- **Owns:** `tokens.json` (the single source), the extractor/emitter, and the CLI (`extract` /
  `generate` / `verify`).
- **Writes into (does not own):**
  - `../../Web/Birko.Web.Components/css/{tokens.css, themes/*.css}` — the byte-identical parity gate.
  - `../Birko.Xaml.Avalonia/Themes/` — six files: `Tokens.{Light,Dark,Neon,Finstat}.axaml` (one
    `ThemeDictionaries` entry each), `Tokens.Brushes.axaml` (shared brushes), and `Tokens.axaml`
    (back-compat aggregate merging all five). One file per theme so a consumer ships only the themes
    it offers — see `Birko.Xaml.Avalonia/CLAUDE.md` § "Theme system" for the composition rules.

## `verify` covers BOTH targets

`verify` diffs the regenerated CSS **and** AXAML against what is on disk, and exits non-zero on any
drift or missing file. Run it before committing anything that touches tokens. It was CSS-only until
the AXAML split turned one generated dictionary into six — six hand-editable files with no gate is
exactly how a stale generated tree goes unnoticed.

**A stale `tokens.json` is the failure mode to watch.** If someone hand-edits the generated CSS (or
AXAML) instead of `tokens.json`, the next `generate` silently *deletes* their edit — this happened:
`--b-color-danger-text` (a WCAG contrast token, all four sheets), an AA-darkened finstat
`--b-text-secondary`, `--b-modal-width-xxl`, `--b-modal-full-inset`, `--b-drawer-width-xxl` and
`--b-input-font-size` had all been hand-added, leaving `CssParityTests` red. The recovery is
`extract` (it folds the live CSS back into `tokens.json` and self-checks the round-trip), then
`generate`. Prevention is running `verify`.

**It happened a second time (2026-08-02), and only the CSS target has ever drifted.** `c97d9bd`
wrote `--b-split-detail-sticky-top` straight into `css/tokens.css`; `e07f9d3` wrote the four dark
`--b-color-*-light` tint fixes straight into `css/themes/dark.css`. Same recovery. Two things this
pinned down, both now closed:

- **Only the CSS drifts because only the CSS lacked a banner.** Every AXAML dictionary opens with
  `AUTO-GENERATED … DO NOT EDIT`; the CSS opened straight into `:root {`, and `dark.css` opened with
  a prose "how to use this theme" comment that reads exactly like a hand-authored file. The banner is
  now emitted into all five sheets (in each `Sheet.prologue`, so the verbatim round-trip carries it
  for free — no emitter change) and `CssParityTests.Every_sheet_declares_itself_generated` fails any
  sheet that ships without it. This matters more than it looks: the output lives in
  **`Birko.Web.Components`, a different git repo**, so the editor's diff, review and test run contain
  nothing that mentions `tokens.json`.
- **`verify` cannot see a stale source.** It answers "does the output match `tokens.json`", never "is
  `tokens.json` still true". At the 2026-08-02 baseline it flagged the two CSS files and passed the
  AXAML — because the AXAML agreed perfectly with the stale source, and was therefore shipping the
  light pastel tints (`#DCFCE7`/`#FEF3C7`/`#FEE2E2`/`#CFFAFE`) that `e07f9d3` had fixed on the web
  side months earlier. A green `verify` is not evidence the tokens are right, only that they are
  consistent. The AXAML defect surfaced only *after* `extract` folded the recovered values back in.

Still open: nothing runs `CssParityTests` when `Birko.Web.Components` changes — none of the three
repos has CI. The banner is a human signal, not a gate.

## Convention deviations (deliberate — do not "fix")

1. **A real `.csproj`, not `.shproj`/`.projitems`.** It is the first buildable assembly in the
   `Birko\Framework` bucket. It is a build-time **tool** (needs an executable), not a runtime
   shared library. It is therefore **not** imported into `Birko.Framework.csproj` (the aggregator
   is for runtime shared projects only). Registered in `Birko.Framework.slnx` (new `/Xaml/`
   folder) and `.code-workspace`. Foreshadows EPIC-015's note that `Birko.Xaml.*` ship as real
   assemblies too.
2. **`tokens.json` is deliberately "dumb" / language-neutral.** No C#-specific constructs, no
   logic in the data. All formatting/emit knowledge lives in the generator code. This keeps the
   documented option of swapping the C# generator for a TypeScript one later cheap: rewrite one
   emitter against the committed CSS as a golden test — the schema and extracted data carry over
   untouched.

## Byte-identical parity — how it holds

- Model: `Sheet` = `prologue` + `{selector} {` + `Body[]` + `}` + `epilogue`. Each body line is
  one `Node`: `var` (`name`/`value`/`trail`) or `raw` (verbatim).
- **Values** are single-sourced on `var.value`. `trail` is the verbatim text after `;` (leading
  spaces + inline comment) — it preserves hand-authored comment-column alignment, which is *not*
  reconstructable from a rule (the source uses per-cluster visual alignment, not a global column).
  Do **not** try to derive alignment; keep it verbatim.
- **Line endings:** the repo is canonically **LF** (checked: `git show HEAD:` bytes are LF for all
  five files; `core.autocrlf=true` may present `tokens.css` as CRLF in a working tree). The model
  is kept LF; `CssIo.Normalize` folds CRLF→LF on read; `verify`/tests compare normalized. This
  makes generation machine-independent — do not switch to per-working-tree EOL.
- Gate: `CssIo.Emit(sheet)` must equal the normalized committed CSS. Enforced by
  `Birko.DesignTokens.Tests` and the `verify` verb.

## AXAML notes

- STORY-029 maps only the unambiguous, high-value tokens: colors (`Color`+`SolidColorBrush`),
  lengths (rem→px baked at 16), fonts (`FontFamily`), simple numerics. `var()` refs resolve
  per-theme so each theme dictionary is full & self-contained — which is precisely what makes the
  per-theme file split work: any subset can be merged without pulling in the others.
- **`ThemeIdKey` (`BThemeId`)** is emitted into every theme dictionary, naming its own theme. It is
  an AXAML-only mechanism (not a design token, absent from the CSS and from `tokens.json`) that lets
  `AvaloniaThemeManager` detect which themes were actually merged. Do not "clean it up": presence
  probing cannot substitute for it, because an omitted variant resolves through its `InheritVariant`
  and would answer anyway.
- Composite/motion tokens (shadows, focus rings, transitions, easings, durations, gradients) and
  the `ThemeVariant`/`DynamicResource` wiring are **STORY-030**. The `inverse` CSS theme (a scoped
  partial) is intentionally **not** emitted to AXAML yet.
- XML comments must not contain `--`; keep CLI flags out of generated comment text.

## Editing tokens

Edit a token's `value` in `tokens.json`, then `dotnet run --project Birko.DesignTokens -- generate`.
Never hand-edit the generated CSS or AXAML. Adding a theme = add a `Sheet`; adding a token = add a
`var` node to each relevant sheet. Re-run `generate`; the tests guard the CSS parity gate.

## Tests

`Birko.DesignTokens.Tests` (xUnit + FluentAssertions, in `Birko\Framework.Tests`): CSS round-trip
parity per sheet, extractor round-trip on the live files, single-source/uniqueness checks, AXAML
well-formedness, per-theme resolution, color/length/key-name conversion unit tests, and
cross-theme key-set parity (swap safety).

**Both generated trees are gated by the suite, not just by the `verify` verb.** `CssParityTests`
covers the CSS; `AxamlParityTests` covers the six AXAML dictionaries (each file must equal what
tokens.json regenerates, plus a check that `Themes/` holds *exactly* the generated set, so a renamed
or dropped dictionary can't linger and keep serving tokens that left the source). AXAML had only the
CLI verb before, and nothing runs a CLI verb on its own — which is how the CSS went stale unnoticed.
